#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.FlightSimulator.SimConnect;

// TaxiPro feasibility test. Aircraft on a long straight (runway), engines running, parking brake OFF.
// Phases: idle creep baseline, hold by zeroing velocity, breakaway (hold until N1 threshold),
// free coast, coast with rolling resistance written into velocity, brake-position and brake-event braking.
enum Def { Read = 1, Title = 2, Vel = 3, BrakePos = 4, Thr = 5 }
enum Req { Read = 1, Title = 2 }
enum Ev { Thr1 = 1, Thr2 = 2, BrakeL = 3, BrakeR = 4, ParkOff = 5 }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Read
{
    public double Gs, Vbz, Vbx, OnGround, Park, ThrLever1, N1_1, N1_2, Weight, BrakeL, BrakeR, AccBodyZ;
}
[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
struct Title { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name; }
[StructLayout(LayoutKind.Sequential, Pack = 1)] struct Vel { public double Z, X; }
[StructLayout(LayoutKind.Sequential, Pack = 1)] struct BrakePos { public double L, R; }
[StructLayout(LayoutKind.Sequential, Pack = 1)] struct Thr { public double T1, T2; }

static class P
{
    static Read r;
    static string title = "?";
    static SimConnect sim = null!;
    static StreamWriter log = null!;
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static int exceptions;

    static void Main(string[] args)
    {
        // Double-clicked (no arguments): show what it sees, ask before driving the aircraft,
        // and keep the window open at the end so the result can be read.
        var interactive = args.Length == 0;
        try
        {
            Run(args, interactive);
        }
        catch (COMException)
        {
            Console.WriteLine("Could not connect to MSFS 2024. Start the sim, load the aircraft, then run this again.");
        }
        if (interactive)
        {
            Console.WriteLine();
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
    }

    static void Run(string[] args, bool interactive)
    {
        Console.WriteLine("TaxiPro feasibility test, by Rotaidem");
        Console.WriteLine();
        log = new StreamWriter(Path.Combine(Environment.CurrentDirectory, "taxipro-feasibility-" + DateTime.Now.ToString("HHmmss") + ".log")) { AutoFlush = true };
        sim = new SimConnect("TaxiPro feasibility test", IntPtr.Zero, 0, null, 0);
        sim.OnRecvException += (s, e) => { exceptions++; Out($"# SIMCONNECT EXCEPTION {e.dwException} send={e.dwSendID} idx={e.dwIndex}"); };
        sim.OnRecvSimobjectData += (s, d) =>
        {
            if (d.dwRequestID == (uint)Req.Read) r = (Read)d.dwData[0];
            else if (d.dwRequestID == (uint)Req.Title) title = ((Title)d.dwData[0]).Name;
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
        Add(Def.Thr, "GENERAL ENG THROTTLE LEVER POSITION:1", "percent");
        Add(Def.Thr, "GENERAL ENG THROTTLE LEVER POSITION:2", "percent");
        sim.RegisterDataDefineStruct<Thr>(Def.Thr);
        sim.MapClientEventToSimEvent(Ev.Thr1, "THROTTLE1_SET");
        sim.MapClientEventToSimEvent(Ev.Thr2, "THROTTLE2_SET");
        sim.MapClientEventToSimEvent(Ev.BrakeL, "AXIS_LEFT_BRAKE_SET");
        sim.MapClientEventToSimEvent(Ev.BrakeR, "AXIS_RIGHT_BRAKE_SET");
        sim.RequestDataOnSimObject(Req.Read, Def.Read, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        sim.RequestDataOnSimObject(Req.Title, Def.Title, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        Pump(1.5);

        Out($"Aircraft: {title}  weight {r.Weight:0} lb  on ground {r.OnGround}  parking brake {r.Park:0.00}  GS {r.Gs:0.0} kt  N1 {r.N1_1:0.0}/{r.N1_2:0.0}");
        if (args.Length > 0 && args[0] == "check")
        {
            Out("check only: nothing was sent to the sim.");
            sim.Dispose();
            return;
        }
        if (r.OnGround < 0.5) { Out("Not on the ground: stopping."); return; }
        if (r.Park > 0.5) { Out("Parking brake is set: release it and run again."); return; }
        if (interactive)
        {
            Console.WriteLine();
            Console.WriteLine("The test throttles up and taxis to about 14 kt several times (about two minutes).");
            Console.WriteLine("The aircraft must be lined up on a RUNWAY, engines running, parking brake released.");
            Console.WriteLine("Keep your hands off the throttles. Close HeavyPro.");
            Console.Write("Type yes to start: ");
            if (!string.Equals((Console.ReadLine() ?? "").Trim(), "yes", StringComparison.OrdinalIgnoreCase))
            {
                Out("Not started: nothing was sent to the sim.");
                sim.Dispose();
                return;
            }
        }

        // ---- 1. Idle creep baseline ----
        Throttle(0);
        var b = Phase("1 idle, no help", 12, null);
        // ---- 2. Hold by zeroing velocity ----
        var h = Phase("2 idle, HOLD (velocity zeroed every frame)", 10, () => SetVel(0));
        // ---- 3. Breakaway: thrust up while holding; release above N1 threshold ----
        double breakawayN1 = 45, releasedAt = -1;
        var t0 = clock.Elapsed.TotalSeconds;
        Throttle(0.55);
        var held = Phase("3a thrust 55 %, HOLD until N1 > 45", 25, () =>
        {
            if (releasedAt < 0 && Math.Max(r.N1_1, r.N1_2) < breakawayN1) SetVel(0);
            else if (releasedAt < 0) { releasedAt = clock.Elapsed.TotalSeconds - t0; Out($"   released at N1 {Math.Max(r.N1_1, r.N1_2):0.0} after {releasedAt:0.0} s"); }
        }, stopWhen: () => r.Gs > 14);
        Out($"   throttle lever reads {r.ThrLever1:0} % (did THROTTLE_SET reach the aircraft?)");
        Throttle(0);
        // ---- 4. Free coast ----
        var coast = Phase("4 idle coast, no help", 8, null, stopWhen: () => r.Gs < 3);
        // re-accelerate a little for the next phase
        Throttle(0.5);
        Phase("   (re-accelerating)", 25, null, stopWhen: () => r.Gs > 14);
        Throttle(0);
        // ---- 5. Coast with rolling resistance written into velocity (1.5 ft/s^2 extra) ----
        double extra = 1.5, lastWrite = clock.Elapsed.TotalSeconds;
        var resist = Phase("5 idle coast + rolling resistance 1.5 ft/s2 via velocity", 8, () =>
        {
            var now = clock.Elapsed.TotalSeconds;
            var dt = Math.Min(0.1, now - lastWrite);
            lastWrite = now;
            var v = r.Vbz; if (v > 0.5) SetVel(Math.Max(0, v - extra * dt));
        }, stopWhen: () => r.Gs < 3);
        // ---- 6. Brake position simvar at 50 % ----
        Throttle(0.5);
        Phase("   (re-accelerating)", 25, null, stopWhen: () => r.Gs > 14);
        Throttle(0);
        var bpos = Phase("6 brakes 50 % via BRAKE LEFT/RIGHT POSITION simvar", 6, () => sim.SetDataOnSimObject(Def.BrakePos, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new BrakePos { L = 0.5, R = 0.5 }), stopWhen: () => r.Gs < 3);
        sim.SetDataOnSimObject(Def.BrakePos, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new BrakePos { L = 0, R = 0 });
        // ---- 7. Brake events at 50 % ----
        Throttle(0.5);
        Phase("   (re-accelerating)", 25, null, stopWhen: () => r.Gs > 14);
        Throttle(0);
        var bev = Phase("7 brakes 50 % via AXIS_LEFT/RIGHT_BRAKE_SET events", 6, () => { Brake(0.5); }, stopWhen: () => r.Gs < 3);
        // ---- stop ----
        Phase("   (stopping with full brake events)", 15, () => Brake(1), stopWhen: () => r.Gs < 0.5);
        Brake(0);
        Throttle(0);
        Pump(0.5);

        Out("");
        Out("===== SUMMARY: " + title + " =====");
        Out($"1 idle creep, no help:        GS {b.StartGs:0.0} -> {b.EndGs:0.0} kt in {b.Seconds:0.0} s");
        Out($"2 hold at idle:               max GS {h.MaxGs:0.00} kt (0 = hold works)");
        Out($"3 breakaway:                  {(releasedAt < 0 ? "never released (N1 never reached 45 %)" : $"held until {releasedAt:0.0} s, max GS while held {held.MaxGsBeforeRelease:0.00} kt")}");
        Out($"4 free coast decel:           {coast.Decel:0.00} kt/s");
        Out($"5 coast + resistance decel:   {resist.Decel:0.00} kt/s (expect about {coast.Decel + extra * 0.5925:0.00})");
        Out($"6 brake POSITION 50 % decel:  {bpos.Decel:0.00} kt/s");
        Out($"7 brake EVENTS 50 % decel:    {bev.Decel:0.00} kt/s");
        Out($"SimConnect exceptions: {exceptions}");
        sim.Dispose();
    }

    sealed class Result { public double StartGs, EndGs, MaxGs, Seconds, Decel, MaxGsBeforeRelease; }

    static Result Phase(string name, double seconds, Action? each, Func<bool>? stopWhen = null)
    {
        Out($"-- {name}");
        var res = new Result { StartGs = r.Gs, MaxGs = r.Gs };
        var start = clock.Elapsed.TotalSeconds;
        double firstGs = r.Gs, firstT = start, lastLog = 0;
        while (clock.Elapsed.TotalSeconds - start < seconds)
        {
            sim.ReceiveMessage();
            each?.Invoke();
            res.MaxGs = Math.Max(res.MaxGs, r.Gs);
            if (Math.Max(r.N1_1, r.N1_2) < 45) res.MaxGsBeforeRelease = Math.Max(res.MaxGsBeforeRelease, r.Gs);
            var t = clock.Elapsed.TotalSeconds - start;
            if (t - lastLog >= 1) { lastLog = t; Out(F("   t={0,4:0.0}s GS {1,5:0.00} kt  Vz {2,6:0.00} ft/s  N1 {3,5:0.0}/{4,5:0.0}  thr {5,4:0}%  brakes {6:0.00}/{7:0.00}  accZ {8,6:0.00}", t, r.Gs, r.Vbz, r.N1_1, r.N1_2, r.ThrLever1, r.BrakeL, r.BrakeR, r.AccBodyZ)); }
            if (stopWhen != null && stopWhen()) break;
            Thread.Sleep(15);
        }
        var dt = clock.Elapsed.TotalSeconds - firstT;
        res.EndGs = r.Gs;
        res.Seconds = dt;
        res.Decel = dt > 0.1 ? (firstGs - r.Gs) / dt : 0;
        return res;
    }

    static void SetVel(double z) => sim.SetDataOnSimObject(Def.Vel, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new Vel { Z = z, X = 0 });

    static void Throttle(double f)
    {
        var v = (uint)Math.Round(Math.Max(0, Math.Min(1, f)) * 16383);
        sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.Thr1, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.Thr2, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        Out($"   throttle -> {f:P0}");
    }

    static void Brake(double f)
    {
        // Axis brake events: -16383 = released, +16383 = full.
        var v = unchecked((uint)(int)Math.Round(-16383 + f * 32766));
        sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.BrakeL, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, Ev.BrakeR, v, (Ev)1u, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    static void Pump(double seconds)
    {
        var start = clock.Elapsed.TotalSeconds;
        while (clock.Elapsed.TotalSeconds - start < seconds) { sim.ReceiveMessage(); Thread.Sleep(15); }
    }

    static string F(string format, params object[] a) => string.Format(CultureInfo.InvariantCulture, format, a);

    static void Out(string line)
    {
        Console.WriteLine(line);
        log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff ") + line);
    }
}
