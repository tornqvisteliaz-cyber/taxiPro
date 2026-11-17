# TaxiPro

Ground handling for Microsoft Flight Simulator 2024, by Rotaidem. A heavy jet should feel heavy on the ground too. TaxiPro sits next to [HeavyPro](https://github.com/tornqvisteliaz-cyber/heavypro), which does the same for the controls in flight.

- **Breakaway thrust:** a parked 300-tonne jet does not creep at idle. It needs a push of thrust (about 30–35 % N1 on a heavy 777) to start rolling, then settles back. Grass, sand and snow need much more.
- **Weight you can feel:** speed builds at *(thrust − rolling resistance) ÷ mass*, so a heavy jet accelerates slowly, coasts a long way at idle, settles to a stop and scrubs speed in tight turns. The **Weight feel** slider sets how strict it is: 0 leaves it to the sim, 100 applies the full physics.
- **Progressive brakes:** toe brakes and the brake keys build pressure smoothly, slower on heavier aircraft. A deadzone means the brakes never stick.
- **Real tiller limits:** the tiller gives full nosewheel authority only when slow. Above 10 kt it fades to the pedals' 7° on Boeings, and on Airbuses it fades to nothing by 70–80 kt. The nosewheel turns at the steering actuator's rate instead of snapping.

> **Status: 0.2.0, not yet run against MSFS.** The physics is unit-tested, but no effect has been confirmed in the sim. Each effect checks live whether the aircraft accepts it. If an aircraft overrides one, TaxiPro stops sending it and says so in the window and the log, rather than fighting the aircraft. Please send the log from a first session in the PMDG 777 and the iniBuilds A350.

## Use it

1. Download [TaxiProSetup.exe](https://github.com/tornqvisteliaz-cyber/taxiPro/releases/download/latest-build/TaxiProSetup.exe) (newest build of `main`) and run it, then press **Install TaxiPro**. No licence key is needed for now. Windows may say *"Windows protected your PC"* because the installer is not code-signed yet: click **More info → Run anyway**. RotaHub users can install TaxiPro from RotaHub instead.
2. Keep your MSFS bindings as they are: toe brakes on **Left/Right brake axis**, the brake key on **Brakes**. A tiller goes on **Steering axis** (the `AXIS_STEERING_SET` binding). TaxiPro captures those inputs the way HeavyPro captures the stick, and passes on anything it isn't shaping at that moment unchanged.
3. Load a flight. TaxiPro connects by itself, recognises the aircraft and shows its ground profile.

**Runs in the background:** closing the window keeps TaxiPro running in the system tray, where its tooltip shows what it is doing. Right-click the icon to open TaxiPro or exit it. **Start with Windows** (offered by the installer and in the app) starts it in the tray when you log in.

**Updating** is running the newest `TaxiProSetup.exe` again (or RotaHub): it keeps your settings. **Uninstall** it from Windows' installed apps. Your settings are kept.

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

**Retry checks** forgets what TaxiPro learnt about the loaded aircraft and tries every effect again. Logs are in `%AppData%\TaxiPro\logs`, and settings in `%AppData%\TaxiPro\settings.json`. TaxiPro is installed in `%LOCALAPPDATA%\TaxiPro`.

### Next to HeavyPro

TaxiPro is made to run at the same time as HeavyPro. You don't need to close either one or change any setting.

| | HeavyPro | TaxiPro |
|---|---|---|
| Stick (pitch, roll) | ✔ | never touched |
| Rudder pedals' rudder axis (and the nosewheel steering it drives) | ✔ | never touched |
| Tiller (`AXIS_STEERING_SET`) | – | ✔ |
| Breakaway thrust and weight (`VELOCITY BODY Z`) | – | ✔ |
| Toe brakes | when HeavyPro shapes them, see below | everything else |
| Brake keys (`BRAKES`, `BRAKES_LEFT/RIGHT`) | – | ✔ (passed through while HeavyPro has the toe brakes) |

**Toe brakes:** both apps use the same brake model (HeavyPro's progressive toe brakes), and only one of them shapes the brakes at a time. TaxiPro steps aside exactly when HeavyPro shapes them. It makes the same decision HeavyPro makes, from the same settings and the same MSFS control profiles:

- HeavyPro is running, with **Master Enable** and **Heavy toe brakes** on, its rudder pedals picked and a toe brake set up with **Detect**;
- and those pedals' toe brakes are not still bound in MSFS. If they are, HeavyPro leaves the brakes to MSFS, so TaxiPro shapes them. HeavyPro's **Fix MSFS bindings** clears that binding and hands the brakes to HeavyPro.

The window shows **● HeavyPro running**; hover over it to see who has the brakes. The log records each change. **Leave the toe brakes to HeavyPro** (on by default) can be turned off.

**Underneath:** each app captures only its own inputs, so the two never mask or resend each other's events. HeavyPro holds the stick and rudder events; TaxiPro holds the toe-brake, brake-key and tiller events. HeavyPro's flight-model option for the PMDG 777 changes only pitch and roll inertia, not the ground physics. Each app has its own settings folder, tray icon and Start with Windows entry, and the two install side by side in `%LOCALAPPDATA%\HeavyPro` and `%LOCALAPPDATA%\TaxiPro`, as RotaHub expects. Close both only for the feasibility test, which writes the brakes itself.

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

## Releases

GitHub Actions builds every push and pull request, and runs the unit tests first. Every build of `main` replaces the files of the `latest-build` pre-release. A tag like `v0.2.0` makes a versioned release, with its notes taken from `CHANGELOG.md`.

| File | What it is |
|---|---|
| `TaxiProSetup.exe` / `TaxiProSetup.zip` | The installer, with the app inside (a zip for networks that block `.exe` downloads) |
| `TaxiPro-rotahub.zip` | The package RotaHub installs: `app\` goes to `%LOCALAPPDATA%\TaxiPro` |
| `rotahub.jws` | Tags only: the release manifest RotaHub trusts. It is signed by `packaging/New-HubManifest.ps1` with Rotaidem's release key (the `UPDATE_SIGNING_KEY` secret, as in HeavyPro), which must match `packaging/update-public-key.json` |
| `TaxiProFeasibility.zip` | The feasibility test |
| `SHA256SUMS.txt` | Checksums of all of the above |

Code signing works as in HeavyPro: `packaging/Sign-File.ps1` signs `TaxiPro.exe` and `TaxiProSetup.exe` once the `CODE_SIGNING_PFX_BASE64` and `CODE_SIGNING_PFX_PASSWORD` secrets exist. Until then it only warns.

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
  Services/HeavyProLink    HeavyPro's own brake rule, from its settings and the MSFS profiles
src/TaxiPro.Core.Tests   xUnit tests (net8.0): dotnet test src/TaxiPro.Core.Tests
src/TaxiPro.App          Windows app (WinForms, .NET Framework 4.8): SimLink, MainForm, tray
src/TaxiPro.Setup        TaxiProSetup.exe: install, update, uninstall
packaging/               Payload, signing and RotaHub manifest scripts; the release public key
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
