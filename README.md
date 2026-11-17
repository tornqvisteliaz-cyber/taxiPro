# TaxiPro

Ground handling for Microsoft Flight Simulator 2024, by Rotaidem. A heavy jet should feel heavy on the ground too. TaxiPro sits next to [HeavyPro](https://github.com/tornqvisteliaz-cyber/heavypro), which does the same for the controls in flight.

- **Breakaway thrust:** a parked 300-tonne jet does not creep at idle. It needs a push of thrust (about 30–35 % N1 on a heavy 777) to start rolling, then settles back. Grass, sand and snow need much more.
- **Weight you can feel:** speed builds at *(thrust − rolling resistance) ÷ mass*, so a heavy jet accelerates slowly, coasts a long way at idle, settles to a stop and scrubs speed in tight turns. The **Weight feel** slider sets how strict it is: 0 leaves it to the sim, 100 applies the full physics.
- **Progressive brakes:** toe brakes and the brake keys build pressure smoothly, slower on heavier aircraft. A deadzone means the brakes never stick.
- **Real tiller limits:** the tiller gives full nosewheel authority only when slow. Above 10 kt it fades to the pedals' 7° on Boeings, and on Airbuses it fades to nothing by 70–80 kt. The nosewheel turns at the steering actuator's rate instead of snapping.

> **Status: 0.1.0, first build, not yet run against MSFS.** The physics is unit-tested (65 tests), but no effect has been confirmed in the sim. Each effect checks live whether the aircraft accepts it. If an aircraft overrides one, TaxiPro stops sending it and says so in the window and the log, rather than fighting the aircraft. Please send the log from a first session in the PMDG 777 and the iniBuilds A350.

## Use it

1. Download [TaxiPro.zip](https://github.com/tornqvisteliaz-cyber/taxiPro/releases/download/latest-build/TaxiPro.zip) (built from `main` by GitHub Actions), unzip it anywhere, and run `TaxiPro.exe`. Or build it yourself: `dotnet build src/TaxiPro.App -c Release`.
2. Keep your MSFS bindings as they are: toe brakes on **Left/Right brake axis**, the brake key on **Brakes**. A tiller goes on **Steering axis** (the `AXIS_STEERING_SET` binding). TaxiPro captures those inputs the way HeavyPro captures the stick, and passes on anything it isn't shaping at that moment unchanged.
3. Load a flight. TaxiPro connects by itself, recognises the aircraft and shows its ground profile.

The badge shows what TaxiPro is doing:

| Badge | Meaning |
|---|---|
| `HOLDING` | Stopped, too little thrust to break away. Add thrust to roll. |
| `TAXIING` | Rolling with the weight model applied. |
| `BRAKING` | Progressive brakes are applying pressure. |
| `PARKED` / `PUSHBACK` / `SLEW` / `IN THE AIR` | TaxiPro is standing by and not touching anything. |
| `TAKEOFF / LANDING` | Above 40 kt. The speed belongs to the sim. |
| `OFF` | The master switch is off. Every input passes straight through. |
| `OFFLINE` | MSFS is not running yet. TaxiPro connects on its own. |

**Retry checks** forgets what TaxiPro learnt about the loaded aircraft and tries every effect again. Logs are in `%AppData%\TaxiPro\logs`, and settings in `%AppData%\TaxiPro\settings.json`.

### Next to HeavyPro

HeavyPro can shape the toe brakes itself. When HeavyPro is running with its toe brakes set up and on, TaxiPro leaves the brakes to it, so the two never write the brakes at the same time. The **Leave the toe brakes to HeavyPro** checkbox turns this off. TaxiPro never touches the stick or the rudder axis, which belong to HeavyPro.

## How it works

TaxiPro reads the aircraft every sim frame over SimConnect: forward speed, weight, thrust per engine, N1, surface, yaw rate, brakes, pushback and slew. `TaxiEngine` decides what to send, and `SimLink` sends it.

| Effect | What TaxiPro sends | Self-check |
|---|---|---|
| Breakaway | Holds `VELOCITY BODY Z` at 0 while thrust is below breakaway, then lets go | A hold that the aircraft overrides is detected: it slips twice, or the speed grows while held. Breakaway and weight are then turned off for that aircraft. |
| Weight | When the sim accelerates faster than thrust − resistance allows, writes a lower `VELOCITY BODY Z`. Only ever lower, never more than 6 ft/s² of extra drag, and never the sideways speed | Same as breakaway |
| Brakes | Captures `AXIS_LEFT/RIGHT_BRAKE_SET` and `BRAKES*`, then writes `BRAKE LEFT/RIGHT POSITION` (HeavyPro's route, which the PMDG 777 accepts) | If the brake position read back does not follow, it switches to sending brake axis events, then to pass-through |
| Tiller | Captures `AXIS_STEERING_SET` and sends `STEERING_SET` where hardware input enters, like HeavyPro's Direct route | Logged |

TaxiPro stays out of the way when the aircraft is in the air, in slew, in pushback, has the parking brake set, or is rolling backwards. It also stays out on aircraft that aren't jets. Between 25 and 40 kt, and at takeoff thrust (N1 above 70–80 %), it hands the speed back to the sim, so a takeoff roll is never touched.

Each type has its own ground profile: PMDG 777 / 737 / 747, iniBuilds A350-900 / -1000, Fenix A320 / A321, A380, 787, 767, A330, A340, A300/A310, MD-11, TriStar, 757, A220, MD-80, 727, E-Jets, CRJ and 146. Any other aircraft gets a profile for its size class.

## Project layout

Same structure as HeavyPro: the physics is a plain library with no SimConnect dependency, so it can be unit-tested anywhere.

```
TaxiPro.sln
src/TaxiPro.Core         Ground physics, profiles, settings (netstandard2.0)
  Physics/GroundMomentum   breakaway + weight model
  Physics/Brakes           progressive brakes (HeavyPro's toe-brake model + brake keys)
  Physics/Steering         speed-scheduled tiller
  Physics/TaxiEngine       runs all three, ownership and self-checks
  Physics/AcceptanceMonitor whether the aircraft takes a write
  Models/GroundProfiles    per-type ground data
  Services/HeavyProLink    who owns the brakes when HeavyPro runs
src/TaxiPro.Core.Tests   xUnit tests (net8.0): dotnet test src/TaxiPro.Core.Tests
src/TaxiPro.App          Windows app (WinForms, .NET Framework 4.8): SimLink + MainForm
tools/FeasibilityTest    The original feasibility test
lib/                     SimConnect runtime from the MSFS 2024 SDK
```

## Feasibility test

`tools/FeasibilityTest` is the automated test that checks whether the sim accepts these effects from outside the aircraft. Its results would settle what TaxiPro's self-checks currently decide live. Download [TaxiProFeasibility.zip](https://github.com/tornqvisteliaz-cyber/taxiPro/releases/download/latest-build/TaxiProFeasibility.zip). In MSFS 2024, line the aircraft up on a runway with the engines running, the parking brake released and HeavyPro and TaxiPro closed. Open `TaxiProFeasibility.exe` and press **Run test**. It takes about two minutes, taxis up to 14 kt several times, and saves its log in Documents\TaxiPro.

| Phase | What it does | Tells us |
|---|---|---|
| 1 | Idle, no help | How much the aircraft creeps at idle |
| 2 | Idle, speed zeroed every frame | Whether a hold works |
| 3 | 55 % thrust, held until N1 > 45 % | Whether a breakaway hold beats thrust and releases cleanly |
| 4 | Idle coast, no help | The aircraft's own rolling deceleration |
| 5 | Idle coast with 1.5 ft/s² extra drag written into its speed | Whether rolling resistance can be added |
| 6 | Brakes 50 % through the brake position variables | Whether brakes can be set directly |
| 7 | Brakes 50 % through brake axis events | Whether brakes can be driven like hardware |
