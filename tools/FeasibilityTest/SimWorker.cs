#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.FlightSimulator.SimConnect;

namespace TaxiPro.Feasibility;

/// <summary>What the window shows about the sim and the aircraft.</summary>
public sealed record SimStatus(bool Connected, string Title, double WeightLb, bool OnGround, bool ParkingBrake, double GroundSpeedKt, double N1, bool Running);

/// <summary>
/// Owns the SimConnect connection on its own thread: connects when MSFS runs, keeps the
/// status live, and runs the feasibility test when asked. All SimConnect calls happen on
/// this thread; the window only reads events.
/// </summary>
public sealed class SimWorker : IDisposable
{
    private enum Def { Read = 1, Title = 2, Vel = 3, BrakePos = 4 }
    private enum Req { Read = 1, Title = 2 }
    private enum Ev { Thr1 = 1, Thr2 = 2, BrakeL = 3, BrakeR = 4 }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Read { public double Gs, Vbz, Vbx, OnGround, Park, ThrLever1, N1_1, N1_2, Weight, BrakeL, BrakeR, AccBodyZ; }
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct Title { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Vel { public double Z, X; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BrakePos { public double L, R; }

    private sealed class Result { public double StartGs, EndGs, MaxGs, Seconds, Decel, MaxGsBeforeRelease; }
    private sealed class StopRequested : Exception { }

    public event Action<string>? Log;
    public event Action<SimStatus>? StatusChanged;
    public event Action<IReadOnlyList<string>>? Finished;

    private readonly Thread _thread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private volatile bool _quit, _runRequested, _stopRequested;
    private SimConnect? _sim;
    private Read _r;
    private string _title = "";
    private int _exceptions;
    private StreamWriter? _log;
    private bool _running;

    public string LogFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaxiPro");

    public SimWorker()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "SimConnect" };
        _thread.Start();
    }

    public void RequestRun() => _runRequested = true;
    public void RequestStop() => _stopRequested = true;

    public void Dispose()
    {
        _stopRequested = true;
        _quit = true;
        _thread.Join(3000);
    }

    // ---------------- Connection and status ----------------
    private void Loop()
    {
        var nextConnect = 0.0;
        var nextStatus = 0.0;
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
                try
                {
                    _sim.ReceiveMessage();
                }
                catch (COMException)
                {
                    Disconnect("MSFS closed.");
                }
            }
            if (now >= nextStatus)
            {
                nextStatus = now + 0.2;
                PublishStatus();
            }
            if (_runRequested && _sim != null)
            {
                _runRequested = false;
                RunTest();
            }
            else if (_runRequested && _sim == null)
            {
                _runRequested = false;
                Say("MSFS 2024 is not connected. Start the sim and load the aircraft first.");
            }
            Thread.Sleep(15);
        }
        Disconnect(null);
    }

    private void TryConnect()
    {
        try
        {
            var sim = new SimConnect("TaxiPro feasibility test", IntPtr.Zero, 0, null, 0);
            sim.OnRecvException += (s, e) => { _exceptions++; Say($"# SimConnect exception {e.dwException} (send {e.dwSendID}, index {e.dwIndex})"); };
            sim.OnRecvQuit += (s, e) => Disconnect("MSFS closed.");
            sim.OnRecvSimobjectData += (s, d) =>
            {
                if (d.dwRequestID == (uint)Req.Read) _r = (Read)d.dwData[0];
                else if (d.dwRequestID == (uint)Req.Title) _title = ((Title)d.dwData[0]).Name;
            };
            void Add(Def def, string name, string unit) => sim.AddToDataDefinition(def, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            Add(Def.Read, "GROUND VELOCITY", "knots");
            Add(Def.Read, "VELOCITY BODY Z", "feet per second");
            Add(Def.Read, "VELOCITY BODY X", "feet per second");
            Add(Def.Read, "SIM ON GROUND", "bool");
            Add(Def.Read, "BRAKE PARKING POSITION", "position");
            Add(Def.Read, "GENERAL ENG THROTTLE LEVER POSITION:1", "percent");
            Add(Def.Read, "TURB ENG N1:1", "percent");
            Add(Def.Read, "TURB ENG N1:2", "percent");
            Add(Def.Read, "TOTAL WEIGHT", "pounds");
            Add(Def.Read, "BRAKE LEFT POSITION", "position");
            Add(Def.Read, "BRAKE RIGHT POSITION", "position");
            Add(Def.Read, "ACCELERATION BODY Z", "feet per second squared");
            sim.RegisterDataDefineStruct<Read>(Def.Read);
            sim.AddToDataDefinition(Def.Title, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            sim.RegisterDataDefineStruct<Title>(Def.Title);
            Add(Def.Vel, "VELOCITY BODY Z", "feet per second");
            Add(Def.Vel, "VELOCITY BODY X", "feet per second");
            sim.RegisterDataDefineStruct<Vel>(Def.Vel);
            Add(Def.BrakePos, "BRAKE LEFT POSITION", "position");
            Add(Def.BrakePos, "BRAKE RIGHT POSITION", "position");
            sim.RegisterDataDefineStruct<BrakePos>(Def.BrakePos);
            sim.MapClientEventToSimEvent(Ev.Thr1, "THROTTLE1_SET");
            sim.MapClientEventToSimEvent(Ev.Thr2, "THROTTLE2_SET");
            sim.MapClientEventToSimEvent(Ev.BrakeL, "AXIS_LEFT_BRAKE_SET");
            sim.MapClientEventToSimEvent(Ev.BrakeR, "AXIS_RIGHT_BRAKE_SET");
            sim.RequestDataOnSimObject(Req.Read, Def.Read, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            // The title again every second, so loading another aircraft shows up.
            sim.RequestDataOnSimObject(Req.Title, Def.Title, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            _sim = sim;
            Say("Connected to MSFS 2024.");
        }
        catch (COMException)
        {
            _sim = null;
        }
    }

    private void Disconnect(string? why)
    {
        if (_sim == null)
            return;
        try { _sim.Dispose(); } catch (Exception) { }
        _sim = null;
        _title = "";
        _r = default;
        if (why != null)
            Say(why);
    }

    private void PublishStatus() =>
        StatusChanged?.Invoke(new SimStatus(_sim != null, _title, _r.Weight, _r.OnGround > 0.5, _r.Park > 0.5, _r.Gs, Math.Max(_r.N1_1, _r.N1_2), _running));

    // ---------------- The test ----------------
    private void RunTest()
    {
        if (_r.OnGround < 0.5) { Say("The aircraft is not on the ground. Nothing was sent."); return; }
        if (_r.Park > 0.5) { Say("The parking brake is set. Release it and press Run test again."); return; }

        Directory.CreateDirectory(LogFolder);
        var file = Path.Combine(LogFolder, "taxipro-feasibility-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        _log = new StreamWriter(file) { AutoFlush = true };
        _running = true;
        _stopRequested = false;
        _exceptions = 0;
        var summary = new List<string>();
        try
        {
            Say($"Aircraft: {_title}  weight {_r.Weight:0} lb  GS {_r.Gs:0.0} kt  N1 {Math.Max(_r.N1_1, _r.N1_2):0.0} %");
            summary = Sequence();
        }
        catch (StopRequested)
        {
            Say("Stopped. Bringing the aircraft to a halt.");
            Throttle(0);
            try { Phase("   (stopping with full brake events)", 15, () => Brake(1), stopWhen: () => _r.Gs < 0.5, stoppable: false); } catch (StopRequested) { }
            Brake(0);
            SetBrakePos(0);
            summary.Add("Stopped before the end: no results.");
        }
        catch (COMException)
        {
            Disconnect("Lost the connection to MSFS during the test.");
            summary.Add("The connection to MSFS was lost: no results.");
        }
        finally
        {
            _running = false;
            Say("Log saved: " + file);
            _log.Dispose();
            _log = null;
            Finished?.Invoke(summary);
        }
    }

    private List<string> Sequence()
    {
        // 1. Idle creep baseline
        Throttle(0);
        var b = Phase("1 idle, no help", 12, null);
        // 2. Hold by zeroing velocity
        var h = Phase("2 idle, HOLD (velocity zeroed every frame)", 10, () => SetVel(0));
        // 3. Breakaway: thrust up while holding; release above the N1 threshold
        double breakawayN1 = 45, releasedAt = -1;
        var t0 = _clock.Elapsed.TotalSeconds;
        Throttle(0.55);
        var held = Phase("3 thrust 55 %, HOLD until N1 > 45", 25, () =>
        {
            var n1 = Math.Max(_r.N1_1, _r.N1_2);
            if (releasedAt < 0 && n1 < breakawayN1) SetVel(0);
            else if (releasedAt < 0) { releasedAt = _clock.Elapsed.TotalSeconds - t0; Say($"   released at N1 {n1:0.0} after {releasedAt:0.0} s"); }
        }, stopWhen: () => _r.Gs > 14);
        var leverReached = _r.ThrLever1;
        Say($"   throttle lever reads {leverReached:0} % (did THROTTLE_SET reach the aircraft?)");
        Throttle(0);
        // 4. Free coast
        var coast = Phase("4 idle coast, no help", 8, null, stopWhen: () => _r.Gs < 3);
        Accelerate();
        // 5. Coast with rolling resistance written into velocity
        double extra = 1.5, lastWrite = _clock.Elapsed.TotalSeconds;
        var resist = Phase("5 idle coast + rolling resistance 1.5 ft/s2 via velocity", 8, () =>
        {
            var now = _clock.Elapsed.TotalSeconds;
            var dt = Math.Min(0.1, now - lastWrite);
            lastWrite = now;
            if (_r.Vbz > 0.5) SetVel(Math.Max(0, _r.Vbz - extra * dt));
        }, stopWhen: () => _r.Gs < 3);
        Accelerate();
        // 6. Brake position variables at 50 %
        var bpos = Phase("6 brakes 50 % via BRAKE LEFT/RIGHT POSITION", 6, () => SetBrakePos(0.5), stopWhen: () => _r.Gs < 3);
        SetBrakePos(0);
        Accelerate();
        // 7. Brake events at 50 %
        var bev = Phase("7 brakes 50 % via AXIS_LEFT/RIGHT_BRAKE_SET events", 6, () => Brake(0.5), stopWhen: () => _r.Gs < 3);
        // Stop
        Phase("   (stopping with full brake events)", 15, () => Brake(1), stopWhen: () => _r.Gs < 0.5);
        Brake(0);
        Throttle(0);

        var lines = new List<string>
        {
            "Aircraft: " + _title,
            F("1  Idle creep, no help:          {0:0.0} -> {1:0.0} kt in {2:0} s", b.StartGs, b.EndGs, b.Seconds),
            F("2  Hold at idle:                 max {0:0.00} kt  ({1})", h.MaxGs, h.MaxGs < 0.3 ? "hold WORKS" : "hold does not hold"),
            releasedAt < 0
                ? "3  Breakaway:                    never released (N1 never reached 45 %)"
                : F("3  Breakaway:                    held until {0:0.0} s, max {1:0.00} kt while held ({2})", releasedAt, held.MaxGsBeforeRelease, held.MaxGsBeforeRelease < 0.5 ? "WORKS" : "slips"),
            F("   Throttle lever reached:        {0:0} % ({1})", leverReached, leverReached > 30 ? "throttle commands work" : "throttle commands IGNORED"),
            F("4  Free coast:                   {0:0.00} kt/s", coast.Decel),
            F("5  Coast + rolling resistance:   {0:0.00} kt/s, expected about {1:0.00} ({2})", resist.Decel, coast.Decel + extra * 0.5925, resist.Decel > coast.Decel + 0.4 ? "resistance WORKS" : "no clear effect"),
            F("6  Brakes 50 % (position vars):  {0:0.00} kt/s", bpos.Decel),
            F("7  Brakes 50 % (axis events):    {0:0.00} kt/s", bev.Decel),
            F("SimConnect exceptions: {0}", _exceptions),
        };
        Say("");
        Say("===== RESULTS =====");
        foreach (var line in lines)
            Say(line);
        return lines;
    }

    private void Accelerate()
    {
        Throttle(0.5);
        Phase("   (re-accelerating)", 25, null, stopWhen: () => _r.Gs > 14);
        Throttle(0);
    }

    private Result Phase(string name, double seconds, Action? each, Func<bool>? stopWhen = null, bool stoppable = true)
    {
        Say($"-- {name}");
        var res = new Result { StartGs = _r.Gs, MaxGs = _r.Gs };
        var start = _clock.Elapsed.TotalSeconds;
        double firstGs = _r.Gs, lastLog = 0;
        while (_clock.Elapsed.TotalSeconds - start < seconds)
        {
            if (stoppable && (_stopRequested || _quit))
                throw new StopRequested();
            _sim!.ReceiveMessage();
            each?.Invoke();
            res.MaxGs = Math.Max(res.MaxGs, _r.Gs);
            if (Math.Max(_r.N1_1, _r.N1_2) < 45) res.MaxGsBeforeRelease = Math.Max(res.MaxGsBeforeRelease, _r.Gs);
            var t = _clock.Elapsed.TotalSeconds - start;
            if (t - lastLog >= 1)
            {
                lastLog = t;
                Say(F("   t={0,4:0.0}s  GS {1,5:0.00} kt  N1 {2,5:0.0}  thr {3,3:0}%  brakes {4:0.00}/{5:0.00}", t, _r.Gs, Math.Max(_r.N1_1, _r.N1_2), _r.ThrLever1, _r.BrakeL, _r.BrakeR));
                PublishStatus();
            }
            if (stopWhen != null && stopWhen())
                break;
            Thread.Sleep(15);
        }
        var dt = _clock.Elapsed.TotalSeconds - start;
        res.EndGs = _r.Gs;
        res.Seconds = dt;
        res.Decel = dt > 0.1 ? (firstGs - _r.Gs) / dt : 0;
        return res;
    }

    private void SetVel(double z) =>
        _sim!.SetDataOnSimObject(Def.Vel, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new Vel { Z = z, X = 0 });

    private void SetBrakePos(double f) =>
        _sim?.SetDataOnSimObject(Def.BrakePos, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new BrakePos { L = f, R = f });

    private void Throttle(double f)
    {
        if (_sim == null) return;
        var v = (uint)Math.Round(Math.Max(0, Math.Min(1, f)) * 16383);
        _sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.Thr1, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        _sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.Thr2, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        Say($"   throttle -> {f:P0}");
    }

    private void Brake(double f)
    {
        if (_sim == null) return;
        // Axis brake events: -16383 = released, +16383 = full.
        var v = unchecked((uint)(int)Math.Round(-16383 + f * 32766));
        _sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.BrakeL, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        _sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.BrakeR, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    private static string F(string format, params object[] a) => string.Format(CultureInfo.InvariantCulture, format, a);

    private void Say(string line)
    {
        _log?.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff ") + line);
        Log?.Invoke(line);
    }
}
