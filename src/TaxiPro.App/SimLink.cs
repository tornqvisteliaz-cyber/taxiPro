using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.FlightSimulator.SimConnect;
using TaxiPro.Core;
using TaxiPro.Core.Models;
using TaxiPro.Core.Physics;
using TaxiPro.Core.Services;

namespace TaxiPro.App;

/// <summary>
/// Owns the SimConnect connection on its own thread: connects when MSFS runs, reads the aircraft
/// every sim frame, runs <see cref="TaxiEngine"/>, and sends what it decides. All SimConnect calls
/// happen on this thread; the window only sets settings and reads <see cref="Status"/>.
///
/// Inputs: the toe-brake axes, the brake keys and the tiller (steering) axis are captured in a
/// maskable notification group, the way HeavyPro captures the stick, so the sim does not apply
/// them raw. Whatever TaxiPro does not own at that moment is passed straight on, unchanged.
/// </summary>
public sealed class SimLink : IDisposable
{
    private enum Def { Core = 1, Engines, Extras, Surface, Pushback, Slew, Title, VelocityWrite, BrakeWrite }
    private enum Req { Core = 1, Engines, Extras, Surface, Pushback, Slew, Title }
    private enum Ev { LeftBrakeAxis = 1, RightBrakeAxis, BrakesKey, BrakesLeftKey, BrakesRightKey, SteeringAxis, SteeringOut }
    private enum Group { Input = 1 }

    // Lower number = earlier. Our group sits at the highest maskable priority (as HeavyPro's does);
    // pass-through and axis-route sends go in at "standard", below it, so they never come back to us.
    private const uint HighestMaskable = 10_000_000;
    private const uint Highest = 1;
    private const uint Standard = 1_900_000_000;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct CorePacket { public double OnGround, Parking, GroundSpeed, VelZ, TotalWeight; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct EnginePacket { public double Count, Type, N1a, N1b, N1c, N1d, Ta, Tb, Tc, Td; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ExtrasPacket { public double YawRate, MaxGross, SimRate, BrakeL, BrakeR; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct OnePacket { public double Value; }
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct TitlePacket
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AtcModel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AtcType;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BrakePacket { public double Left, Right; }

    private readonly Logger _log;
    private readonly TaxiEngine _engine = new();
    private readonly Thread _thread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Dictionary<uint, (Def Def, string Name)> _sent = new();
    private readonly HashSet<Def> _broken = new();
    private volatile bool _quit;
    private volatile bool _resetRequested;
    private volatile TaxiSettings _settings;
    private SimConnect? _sim;

    // Latest readings.
    private CorePacket _core;
    private EnginePacket _engines;
    private ExtrasPacket _extras = new() { SimRate = 1 };
    private int _surface = Surface.Unknown;
    private bool _pushback, _slew;
    private AircraftIdentity _aircraft = AircraftIdentity.None;
    private bool _haveCore;

    // Captured inputs.
    private int _leftAxis = -16383, _rightAxis = -16383, _steerAxis;
    private bool _brakeSeen, _tillerSeen;
    private bool _ownedBrakes, _ownedTiller;

    // HeavyPro, checked every few seconds.
    private double _nextHeavyProCheck;
    private HeavyProBrakes _heavyPro = HeavyProBrakes.NotRunning;

    private double _lastStep = -1, _nextPublish;
    private TaxiOutput? _last;
    private string _lastBadge = "", _lastDetail = "", _lastBrakeNote = "";
    private Acceptance _lastSpeedAcceptance, _lastBrakeAcceptance;
    private BrakeRoute _lastBrakeRoute;
    private string _lastProfile = "";

    public SimLink(TaxiSettings settings, Logger log)
    {
        _settings = Copy(settings);
        _log = log;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SimConnect" };
        _thread.Start();
    }

    public event Action<LiveStatus>? Status;

    public void UpdateSettings(TaxiSettings settings) => _settings = Copy(settings);

    /// <summary>Forget what was learnt about this aircraft and try every effect again.</summary>
    public void RetryChecks() => _resetRequested = true;

    public void Dispose()
    {
        _quit = true;
        _signal.Set();
        _thread.Join(3000);
    }

    // ---------------- Connection ----------------
    private void Loop()
    {
        var nextConnect = 0.0;
        while (!_quit)
        {
            var now = _clock.Elapsed.TotalSeconds;
            if (_sim == null && now >= nextConnect)
            {
                nextConnect = now + 3;
                TryConnect();
            }
            if (_sim != null)
            {
                _signal.WaitOne(50);
                try
                {
                    _sim?.ReceiveMessage();
                }
                catch (COMException)
                {
                    Disconnect("MSFS closed.");
                }
            }
            else
            {
                Thread.Sleep(100);
            }
            if (now >= _nextHeavyProCheck)
            {
                _nextHeavyProCheck = now + 3;
                CheckHeavyPro();
            }
            if (now >= _nextPublish)
            {
                _nextPublish = now + 0.1;
                Publish();
            }
        }
        Disconnect(null);
    }

    private void TryConnect()
    {
        SimConnect? sim = null;
        try
        {
            sim = new SimConnect("TaxiPro", IntPtr.Zero, 0, _signal, 0);
            _sim = sim;
            _sent.Clear();
            _broken.Clear();
            sim.OnRecvQuit += (_, _) => Disconnect("MSFS closed.");
            sim.OnRecvException += OnException;
            sim.OnRecvSimobjectData += OnData;
            sim.OnRecvEvent += OnEvent;
            Register(sim);
            _log.Info("Connected to MSFS 2024.");
        }
        catch (COMException)
        {
            try { sim?.Dispose(); } catch (Exception) { }
            _sim = null;
        }
    }

    private void Register(SimConnect sim)
    {
        Add(Def.Core, "SIM ON GROUND", "bool");
        Add(Def.Core, "BRAKE PARKING POSITION", "position");
        Add(Def.Core, "GROUND VELOCITY", "knots");
        Add(Def.Core, "VELOCITY BODY Z", "feet per second");
        Add(Def.Core, "TOTAL WEIGHT", "pounds");
        sim.RegisterDataDefineStruct<CorePacket>(Def.Core);

        Add(Def.Engines, "NUMBER OF ENGINES", "number");
        Add(Def.Engines, "ENGINE TYPE", "enum");
        for (var i = 1; i <= 4; i++) Add(Def.Engines, $"TURB ENG N1:{i}", "percent");
        for (var i = 1; i <= 4; i++) Add(Def.Engines, $"TURB ENG JET THRUST:{i}", "pounds");
        sim.RegisterDataDefineStruct<EnginePacket>(Def.Engines);

        Add(Def.Extras, "ROTATION VELOCITY BODY Y", "degrees per second");
        Add(Def.Extras, "MAX GROSS WEIGHT", "pounds");
        Add(Def.Extras, "SIMULATION RATE", "number");
        Add(Def.Extras, "BRAKE LEFT POSITION", "position");
        Add(Def.Extras, "BRAKE RIGHT POSITION", "position");
        sim.RegisterDataDefineStruct<ExtrasPacket>(Def.Extras);

        // One variable per definition: if MSFS rejects one, it cannot shift the others.
        Add(Def.Surface, "SURFACE TYPE", "enum");
        sim.RegisterDataDefineStruct<OnePacket>(Def.Surface);
        Add(Def.Pushback, "PUSHBACK STATE", "enum");
        sim.RegisterDataDefineStruct<OnePacket>(Def.Pushback);
        Add(Def.Slew, "IS SLEW ACTIVE", "bool");
        sim.RegisterDataDefineStruct<OnePacket>(Def.Slew);

        AddString(Def.Title, "TITLE");
        AddString(Def.Title, "ATC MODEL");
        AddString(Def.Title, "ATC TYPE");
        sim.RegisterDataDefineStruct<TitlePacket>(Def.Title);

        // Writes: the forward speed only (never the sideways speed, which turns need), and the brakes.
        Add(Def.VelocityWrite, "VELOCITY BODY Z", "feet per second");
        sim.RegisterDataDefineStruct<OnePacket>(Def.VelocityWrite);
        Add(Def.BrakeWrite, "BRAKE LEFT POSITION", "position");
        Add(Def.BrakeWrite, "BRAKE RIGHT POSITION", "position");
        sim.RegisterDataDefineStruct<BrakePacket>(Def.BrakeWrite);

        void Every(Req req, Def def, SIMCONNECT_PERIOD period, SIMCONNECT_DATA_REQUEST_FLAG flag) =>
            sim.RequestDataOnSimObject(req, def, SimConnect.SIMCONNECT_OBJECT_ID_USER, period, flag, 0, 0, 0);
        Every(Req.Core, Def.Core, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT);
        Every(Req.Engines, Def.Engines, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT);
        Every(Req.Extras, Def.Extras, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT);
        Every(Req.Surface, Def.Surface, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT);
        Every(Req.Pushback, Def.Pushback, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED);
        Every(Req.Slew, Def.Slew, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT);
        Every(Req.Title, Def.Title, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED);

        // Inputs, captured (masked) so the sim does not apply them raw.
        sim.MapClientEventToSimEvent(Ev.LeftBrakeAxis, "AXIS_LEFT_BRAKE_SET");
        sim.MapClientEventToSimEvent(Ev.RightBrakeAxis, "AXIS_RIGHT_BRAKE_SET");
        sim.MapClientEventToSimEvent(Ev.BrakesKey, "BRAKES");
        sim.MapClientEventToSimEvent(Ev.BrakesLeftKey, "BRAKES_LEFT");
        sim.MapClientEventToSimEvent(Ev.BrakesRightKey, "BRAKES_RIGHT");
        sim.MapClientEventToSimEvent(Ev.SteeringAxis, "AXIS_STEERING_SET");
        sim.MapClientEventToSimEvent(Ev.SteeringOut, "STEERING_SET");
        foreach (var ev in new[] { Ev.LeftBrakeAxis, Ev.RightBrakeAxis, Ev.BrakesKey, Ev.BrakesLeftKey, Ev.BrakesRightKey, Ev.SteeringAxis })
            sim.AddClientEventToNotificationGroup(Group.Input, ev, true);
        sim.SetNotificationGroupPriority(Group.Input, HighestMaskable);
    }

    private void Add(Def def, string name, string unit)
    {
        _sim!.AddToDataDefinition(def, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
        _sent[_sim.GetLastSentPacketID()] = (def, name);
    }

    private void AddString(Def def, string name)
    {
        _sim!.AddToDataDefinition(def, name, null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
        _sent[_sim.GetLastSentPacketID()] = (def, name);
    }

    private void OnException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION e)
    {
        if (_sent.TryGetValue(e.dwSendID, out var what))
        {
            // A rejected variable would shift every value after it: stop trusting that definition.
            if (_broken.Add(what.Def))
                _log.Warn($"MSFS rejected {what.Name} ({(SIMCONNECT_EXCEPTION)e.dwException}); {Explain(what.Def)}");
        }
        else
        {
            _log.Warn($"SimConnect exception {(SIMCONNECT_EXCEPTION)e.dwException} (send {e.dwSendID}, index {e.dwIndex}).");
        }
    }

    private static string Explain(Def def) => def switch
    {
        Def.Core => "TaxiPro cannot read the basics and will write nothing.",
        Def.Engines => "thrust is estimated without N1, so breakaway and weight are off.",
        Def.VelocityWrite => "breakaway and weight are off.",
        Def.BrakeWrite => "brakes use the axis route.",
        _ => "that reading is left out."
    };

    private void Disconnect(string? why)
    {
        var sim = _sim;
        if (sim == null)
            return;
        _sim = null;
        try { sim.Dispose(); } catch (Exception) { }
        _haveCore = false;
        _aircraft = AircraftIdentity.None;
        _brakeSeen = _tillerSeen = false;
        _ownedBrakes = _ownedTiller = false;
        _lastStep = -1;
        _last = null;
        if (why != null)
            _log.Info(why);
    }

    private void CheckHeavyPro()
    {
        try
        {
            var running = Process.GetProcessesByName(HeavyProLink.ProcessName).Length > 0;
            var json = running && File.Exists(HeavyProLink.SettingsPath) ? File.ReadAllText(HeavyProLink.SettingsPath) : null;
            var now = HeavyProLink.Brakes(running, json, running ? MsfsProfiles() : Array.Empty<string>());
            if (now.Running != _heavyPro.Running)
                _log.Info(now.Running ? "HeavyPro is running: TaxiPro never touches the stick or the rudder, which are HeavyPro's." : "HeavyPro closed.");
            if (now.Why != _heavyPro.Why && now.Running)
                _log.Info(now.OwnsBrakes ? "HeavyPro is shaping the toe brakes: TaxiPro leaves them to it." : now.Why + ": TaxiPro shapes the brakes MSFS sends.");
            _heavyPro = now;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
        }
    }

    /// <summary>
    /// The MSFS 2024 control profiles, from the same place HeavyPro reads them:
    /// Steam\userdata\*\2537590\remote\inputprofile_*. Empty when MSFS is not the Steam version.
    /// </summary>
    private static IEnumerable<string> MsfsProfiles()
    {
        var result = new List<string>();
        var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam))
            return result;
        var userdata = Path.Combine(steam!.Replace('/', '\\'), "userdata");
        if (!Directory.Exists(userdata))
            return result;
        foreach (var user in Directory.GetDirectories(userdata))
        {
            var remote = Path.Combine(user, HeavyProLink.MsfsSteamAppId, "remote");
            if (!Directory.Exists(remote))
                continue;
            foreach (var file in Directory.GetFiles(remote, "inputprofile_*"))
            {
                try { result.Add(File.ReadAllText(file)); }
                catch (IOException) { }
            }
        }
        return result;
    }

    // ---------------- Inputs ----------------
    private void OnEvent(SimConnect sender, SIMCONNECT_RECV_EVENT e)
    {
        var value = unchecked((int)e.dwData);
        switch ((Ev)e.uEventID)
        {
            case Ev.LeftBrakeAxis:
                _leftAxis = value;
                _brakeSeen = true;
                if (!_ownedBrakes) Forward(Ev.LeftBrakeAxis, value);
                break;
            case Ev.RightBrakeAxis:
                _rightAxis = value;
                _brakeSeen = true;
                if (!_ownedBrakes) Forward(Ev.RightBrakeAxis, value);
                break;
            case Ev.BrakesKey:
            case Ev.BrakesLeftKey:
            case Ev.BrakesRightKey:
                _brakeSeen = true;
                if (_ownedBrakes)
                    _engine.PressBrakeKey((Ev)e.uEventID == Ev.BrakesLeftKey ? BrakeKey.Left : (Ev)e.uEventID == Ev.BrakesRightKey ? BrakeKey.Right : BrakeKey.Both);
                else
                    Forward((Ev)e.uEventID, value);
                break;
            case Ev.SteeringAxis:
                _steerAxis = value;
                _tillerSeen = true;
                if (!_ownedTiller) Forward(Ev.SteeringAxis, value);
                break;
        }
    }

    /// <summary>Passes a captured event on unchanged, below our own group, as if TaxiPro were not there.</summary>
    private void Forward(Ev ev, int value) => Send(ev, value, Standard);

    private void Send(Ev ev, int value, uint priority)
    {
        try
        {
            _sim?.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, ev, unchecked((uint)value), (Group)priority, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        }
        catch (COMException) { }
    }

    // ---------------- Each frame ----------------
    private void OnData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA d)
    {
        switch ((Req)d.dwRequestID)
        {
            case Req.Core: _core = (CorePacket)d.dwData[0]; _haveCore = true; Step(); break;
            case Req.Engines: _engines = (EnginePacket)d.dwData[0]; break;
            case Req.Extras: _extras = (ExtrasPacket)d.dwData[0]; break;
            case Req.Surface: _surface = (int)Math.Round(((OnePacket)d.dwData[0]).Value); break;
            case Req.Pushback: _pushback = Math.Round(((OnePacket)d.dwData[0]).Value) is 0 or 1 or 2; break;
            case Req.Slew: _slew = ((OnePacket)d.dwData[0]).Value > 0.5; break;
            case Req.Title:
                var t = (TitlePacket)d.dwData[0];
                _aircraft = new AircraftIdentity(t.Title ?? "", t.AtcModel ?? "", t.AtcType ?? "");
                _log.Info($"Aircraft: {_aircraft.DisplayName} ({AircraftIdentity.Readable(t.AtcModel)}).");
                break;
        }
    }

    private void Step()
    {
        if (_sim == null || _broken.Contains(Def.Core))
            return;
        if (_resetRequested)
        {
            _resetRequested = false;
            _engine.ResetChecks();
            _log.Info("Checks reset: every effect is tried again on this aircraft.");
        }
        var now = _clock.Elapsed.TotalSeconds;
        var dt = _lastStep < 0 ? 1.0 / 30 : now - _lastStep;
        _lastStep = now;

        var snapshot = Snapshot();
        var settings = _settings;
        if (_broken.Contains(Def.Engines) || _broken.Contains(Def.VelocityWrite))
            settings = Copy(settings, s => { s.Breakaway = false; s.Weight = false; });
        var input = new TaxiInputs(Brakes.PedalFromAxis(_leftAxis), Brakes.PedalFromAxis(_rightAxis), Steering.TillerFromAxis(_steerAxis));
        var o = _engine.Step(snapshot, input, settings, _heavyPro.OwnsBrakes, dt);
        _last = o;

        try
        {
            if (o.Speed.Write)
                _sim.SetDataOnSimObject(Def.VelocityWrite, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new OnePacket { Value = o.Speed.VelocityBodyZ });

            if (o.OwnsBrakes && o.Brakes.Active)
            {
                if (o.BrakeRoute == BrakeRoute.Position && !_broken.Contains(Def.BrakeWrite))
                    _sim.SetDataOnSimObject(Def.BrakeWrite, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new BrakePacket { Left = o.Brakes.Left, Right = o.Brakes.Right });
                else
                {
                    Send(Ev.LeftBrakeAxis, Brakes.AxisFromPedal(o.Brakes.Left), Standard);
                    Send(Ev.RightBrakeAxis, Brakes.AxisFromPedal(o.Brakes.Right), Standard);
                }
            }
            if (_ownedBrakes && !o.OwnsBrakes)
            {
                // Handing the brakes back: the sim gets the pedals as they are now.
                Forward(Ev.LeftBrakeAxis, _leftAxis);
                Forward(Ev.RightBrakeAxis, _rightAxis);
            }
            _ownedBrakes = o.OwnsBrakes;

            if (o.OwnsTiller && o.Steering.Active)
                Send(Ev.SteeringOut, Steering.AxisFromPosition(o.Steering.Position), Highest); // where hardware enters, like HeavyPro's Direct route
            if (_ownedTiller && !o.OwnsTiller && _tillerSeen)
                Forward(Ev.SteeringAxis, _steerAxis);
            _ownedTiller = o.OwnsTiller;
        }
        catch (COMException)
        {
            Disconnect("Lost the connection to MSFS.");
            return;
        }
        LogChanges(o);
    }

    private GroundSnapshot Snapshot()
    {
        var e = _broken.Contains(Def.Engines) ? default : _engines;
        var x = _broken.Contains(Def.Extras) ? new ExtrasPacket { SimRate = 1 } : _extras;
        var count = (int)Numeric.Clamp(Math.Round(Numeric.Finite(e.Count)), 0, 4);
        var n1 = new[] { e.N1a, e.N1b, e.N1c, e.N1d };
        var thrust = new[] { e.Ta, e.Tb, e.Tc, e.Td };
        double sumThrust = 0, maxN1 = 0;
        for (var i = 0; i < count; i++)
        {
            sumThrust += Math.Max(0, Numeric.Finite(thrust[i]));
            maxN1 = Math.Max(maxN1, Numeric.Finite(n1[i]));
        }
        return new GroundSnapshot
        {
            Aircraft = _aircraft,
            OnGround = _core.OnGround > 0.5,
            ParkingBrakeSet = _core.Parking > 0.5,
            GroundSpeedKnots = Numeric.Finite(_core.GroundSpeed),
            VelocityBodyZ = Numeric.Finite(_core.VelZ),
            TotalWeightPounds = Numeric.Finite(_core.TotalWeight),
            YawRateDegPerSec = Numeric.Finite(x.YawRate),
            MaxGrossWeightPounds = Numeric.Finite(x.MaxGross),
            SimulationRate = x.SimRate > 0 ? x.SimRate : 1,
            BrakeLeft = Numeric.Finite(x.BrakeL),
            BrakeRight = Numeric.Finite(x.BrakeR),
            EngineCount = count,
            EngineType = _broken.Contains(Def.Engines) ? -1 : (int)Math.Round(Numeric.Finite(e.Type, -1)),
            MaxN1Percent = maxN1,
            // Engines running but no thrust reported: let TaxiPro estimate it from N1.
            ThrustPounds = sumThrust > 0 || maxN1 < 5 ? sumThrust : double.NaN,
            SurfaceType = _broken.Contains(Def.Surface) ? Surface.Unknown : _surface,
            PushbackActive = !_broken.Contains(Def.Pushback) && _pushback,
            SlewActive = !_broken.Contains(Def.Slew) && _slew,
        };
    }

    private void LogChanges(TaxiOutput o)
    {
        if (o.Profile.Name != _lastProfile)
        {
            _lastProfile = o.Profile.Name;
            _log.Info($"Ground profile: {o.Profile.Name} — tiller {o.Profile.TillerDegrees:0}°, pedals {o.Profile.PedalSteerDegrees:0}°, breakaway {o.Profile.BreakawayCoefficient:P1} of weight.");
        }
        if (o.Badge != _lastBadge || o.Speed.Detail != _lastDetail)
        {
            _lastBadge = o.Badge;
            _lastDetail = o.Speed.Detail;
            _log.Info($"{o.Badge}: {o.Speed.Detail}  (GS {_core.GroundSpeed:0.0} kt, thrust {o.Speed.ThrustPounds:N0} / breakaway {o.Speed.BreakawayPounds:N0} lbf)");
        }
        if (o.BrakeNote != _lastBrakeNote)
        {
            _lastBrakeNote = o.BrakeNote;
            _log.Info("Brakes: " + o.BrakeNote);
        }
        if (_engine.SpeedAcceptance != _lastSpeedAcceptance)
        {
            _lastSpeedAcceptance = _engine.SpeedAcceptance;
            _log.Info(_lastSpeedAcceptance switch
            {
                Acceptance.Works => "Speed writes: this aircraft takes them (the hold holds).",
                Acceptance.Ignored => "Speed writes: this aircraft overrides them. Breakaway and weight are off for it; brakes and tiller carry on.",
                _ => "Speed writes: not checked yet."
            });
        }
        if (_engine.BrakeAcceptance != _lastBrakeAcceptance || _engine.BrakeRoute != _lastBrakeRoute)
        {
            _lastBrakeAcceptance = _engine.BrakeAcceptance;
            _lastBrakeRoute = _engine.BrakeRoute;
            _log.Info($"Brake route: {_lastBrakeRoute}, {_lastBrakeAcceptance}.");
        }
    }

    private void Publish()
    {
        var o = _last;
        var connected = _sim != null;
        Status?.Invoke(!connected || !_haveCore || o == null
            ? LiveStatus.Offline with { Connected = connected, Badge = connected ? "WAITING" : "OFFLINE", HeavyProRunning = _heavyPro.Running, HeavyProNote = _heavyPro.Why }
            : new LiveStatus
            {
                Connected = true,
                Aircraft = _aircraft.DisplayName,
                Profile = o.Profile.Name,
                WeightPounds = _core.TotalWeight,
                OnGround = _core.OnGround > 0.5,
                GroundSpeedKnots = _core.GroundSpeed,
                N1 = Math.Max(Math.Max(_engines.N1a, _engines.N1b), Math.Max(_engines.N1c, _engines.N1d)),
                Surface = Surface.Name(_surface),
                Badge = o.Badge,
                SpeedDetail = o.Speed.Detail,
                ThrustPounds = o.Speed.ThrustPounds,
                BreakawayPounds = o.Speed.BreakawayPounds,
                ExtraDecel = o.Speed.ExtraDecel,
                SpeedAcceptance = _engine.SpeedAcceptance,
                BrakeLeft = o.OwnsBrakes ? o.Brakes.Left : _extras.BrakeL,
                BrakeRight = o.OwnsBrakes ? o.Brakes.Right : _extras.BrakeR,
                BrakeNote = o.BrakeNote,
                BrakeRoute = o.BrakeRoute,
                BrakeAcceptance = _engine.BrakeAcceptance,
                BrakeInputSeen = _brakeSeen,
                TillerInput = Steering.TillerFromAxis(_steerAxis),
                NosewheelDegrees = o.Steering.Position * o.Profile.TillerDegrees,
                TillerDegrees = o.Profile.TillerDegrees,
                TillerNote = o.TillerNote,
                TillerInputSeen = _tillerSeen,
                HeavyProRunning = _heavyPro.Running,
                HeavyProNote = _heavyPro.Why,
            });
    }

    private static TaxiSettings Copy(TaxiSettings s, Action<TaxiSettings>? change = null)
    {
        var copy = new TaxiSettings
        {
            MasterEnable = s.MasterEnable,
            Breakaway = s.Breakaway,
            Weight = s.Weight,
            WeightFeel = s.WeightFeel,
            Brakes = s.Brakes,
            Tiller = s.Tiller,
            YieldBrakesToHeavyPro = s.YieldBrakesToHeavyPro,
            RunInBackground = s.RunInBackground,
        };
        change?.Invoke(copy);
        return copy.Normalised();
    }
}
