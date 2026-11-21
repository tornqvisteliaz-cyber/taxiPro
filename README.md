# GroundPro

Ground handling for Microsoft Flight Simulator 2024, by Rotaidem. A heavy jet should feel heavy on the ground too. GroundPro sits next to [HeavyPro](https://github.com/tornqvisteliaz-cyber/heavypro), which does the same for the controls in flight.

- **Breakaway thrust:** a parked 300-tonne jet does not creep at idle. It needs a push of thrust (about 30–35 % N1 on a heavy 777) to start rolling, then settles back. Grass, sand and snow need much more.
- **Weight you can feel:** speed builds at *(thrust − rolling resistance) ÷ mass*, so a heavy jet accelerates slowly, coasts a long way at idle, settles to a stop and scrubs speed in tight turns. The **Weight feel** slider sets how strict it is: 0 leaves it to the sim, 100 applies the full physics.
- **Progressive brakes:** toe brakes and the brake keys build pressure smoothly, slower on heavier aircraft. A deadzone means the brakes never stick.
- **Real tiller limits:** the tiller gives full nosewheel authority only when slow. Above 10 kt it fades to the pedals' 7° on Boeings, and on Airbuses it fades to nothing by 70–80 kt. The nosewheel turns at the steering actuator's rate instead of snapping.

> **Status: 0.2.0, not yet run against MSFS.** The physics is unit-tested, but no effect has been confirmed in the sim. Each effect checks live whether the aircraft accepts it. If an aircraft overrides one, GroundPro stops sending it and says so in the window and the log, rather than fighting the aircraft. Please send the log from a first session in the PMDG 777 and the iniBuilds A350.

## Use it

1. Download [GroundProSetup.exe](https://github.com/tornqvisteliaz-cyber/groundPro/releases/download/latest-build/GroundProSetup.exe) (newest build of `main`) and run it. Enter your licence key and press **Install GroundPro**. Windows may say *"Windows protected your PC"* because the installer is not code-signed yet: click **More info → Run anyway**. RotaHub users can install GroundPro from RotaHub instead.
2. Keep your MSFS bindings as they are: toe brakes on **Left/Right brake axis**, the brake key on **Brakes**. A tiller goes on **Steering axis** (the `AXIS_STEERING_SET` binding). GroundPro captures those inputs the way HeavyPro captures the stick, and passes on anything it isn't shaping at that moment unchanged.
3. Load a flight. GroundPro connects by itself, recognises the aircraft and shows its ground profile.

**Runs in the background:** closing the window keeps GroundPro running in the system tray, where its tooltip shows what it is doing. Right-click the icon to open GroundPro or exit it. **Start with Windows** (offered by the installer and in the app) starts it in the tray when you log in.

**Updating** is running the newest `GroundProSetup.exe` again (or RotaHub): it keeps your key and settings. **Uninstall** it from Windows' installed apps. Your settings are kept, and the uninstaller offers to free the licence activation.

The badge shows what GroundPro is doing:

| Badge | Meaning |
|---|---|
| `HOLDING` | Stopped, too little thrust to break away. Add thrust to roll. |
| `TAXIING` | Rolling with the weight model applied. |
| `BRAKING` | Progressive brakes are applying pressure. |
| `PARKED` / `PUSHBACK` / `SLEW` / `IN THE AIR` | GroundPro is standing by and not touching anything. |
| `TAKEOFF / LANDING` | Above 40 kt. The speed belongs to the sim. |
| `OFF` | The master switch is off. Every input passes straight through. |
| `NOT LICENSED` | The licence did not check out. Every input passes straight through. |
| `OFFLINE` | MSFS is not running yet. GroundPro connects on its own. |

**Retry checks** forgets what GroundPro learnt about the loaded aircraft and tries every effect again. Logs are in `%AppData%\GroundPro\logs`, and settings in `%AppData%\GroundPro\settings.json`. GroundPro is installed in `%LOCALAPPDATA%\GroundPro`.

### Next to HeavyPro

HeavyPro can shape the toe brakes itself. When HeavyPro is running with its toe brakes set up and on, GroundPro leaves the brakes to it, so the two never write the brakes at the same time. The **Leave the toe brakes to HeavyPro** checkbox turns this off. GroundPro never touches the stick or the rudder axis, which belong to HeavyPro.

## How it works

GroundPro reads the aircraft every sim frame over SimConnect: forward speed, weight, thrust per engine, N1, surface, yaw rate, brakes, pushback and slew. `TaxiEngine` decides what to send, and `SimLink` sends it.

| Effect | What GroundPro sends | Self-check |
|---|---|---|
| Breakaway | Holds `VELOCITY BODY Z` at 0 while thrust is below breakaway, then lets go | A hold that the aircraft overrides is detected: it slips twice, or the speed grows while held. Breakaway and weight are then turned off for that aircraft. |
| Weight | When the sim accelerates faster than thrust − resistance allows, writes a lower `VELOCITY BODY Z`. Only ever lower, never more than 6 ft/s² of extra drag, and never the sideways speed | Same as breakaway |
| Brakes | Captures `AXIS_LEFT/RIGHT_BRAKE_SET` and `BRAKES*`, then writes `BRAKE LEFT/RIGHT POSITION` (HeavyPro's route, which the PMDG 777 accepts) | If the brake position read back does not follow, it switches to sending brake axis events, then to pass-through |
| Tiller | Captures `AXIS_STEERING_SET` and sends `STEERING_SET` where hardware input enters, like HeavyPro's Direct route | Logged |

GroundPro stays out of the way when the aircraft is in the air, in slew, in pushback, has the parking brake set, or is rolling backwards. It also stays out on aircraft that aren't jets. Between 25 and 40 kt, and at takeoff thrust (N1 above 70–80 %), it hands the speed back to the sim, so a takeoff roll is never touched.

Each type has its own ground profile: PMDG 777 / 737 / 747, iniBuilds A350-900 / -1000, Fenix A320 / A321, A380, 787, 767, A330, A340, A300/A310, MD-11, TriStar, 757, A220, MD-80, 727, E-Jets, CRJ and 146. Any other aircraft gets a profile for its size class.

## Licensing

GroundPro uses the same licensing as HeavyPro, from the shared `Rotaidem.Licensing` library (copied from HeavyPro; see [its README](src/Rotaidem.Licensing/README.md)).

- **Keys** look like `GPRO-XXXX-XXXX-XXXX`. The check character catches typing mistakes before anything is sent.
- **Activation:** the installer, or the **Activate GroundPro** window when the app has no working key, activates the key on rotaidem.com for this PC. The product id is `rotaidem-groundpro`, with `groundpro` also accepted.
- **Signed answers:** GroundPro trusts a "valid" answer only if it carries a token signed by rotaidem.com's private key, naming this PC, this key and the random nonce GroundPro just sent. The public key is built in from `packaging/licence-public-key.json`, the same server key HeavyPro uses. A fake server, an edited or copied `license.json`, a replayed answer and a clock turned back are all refused.
- **Offline:** a saved signed answer starts GroundPro at once, for up to 14 days between online checks (or the server's lease, if shorter).
- **While running:** GroundPro re-checks every 20 minutes. GroundPro writes nothing unless a server-confirmed lease is running. That is checked in two independent places, `TaxiEngine` and the SimConnect write path. A revoked licence locks it at once and closes it.
- **Moving PCs:** **Deactivate this PC** in the app (or the uninstaller) frees the activation.
- **Debug builds skip the check**, so F5 works without a key. Release builds, which are all that ship, always check.

Keys are saved in `%LOCALAPPDATA%\Rotaidem\groundpro\license.json`, and licence rejections are logged in `%AppData%\GroundPro\logs\licence.log`.

**Before selling GroundPro**, the website needs a `rotaidem-groundpro` product to issue `GPRO` keys, and RotaHub needs to list `rotaidem-groundpro`. Until then every key is refused with "wrong product" or "not found".

## Releases

GitHub Actions builds every push and pull request, and runs the unit tests first. Every build of `main` replaces the files of the `latest-build` pre-release. A tag like `v0.2.0` makes a versioned release, with its notes taken from `CHANGELOG.md`.

| File | What it is |
|---|---|
| `GroundProSetup.exe` / `GroundProSetup.zip` | The installer, with the app inside (a zip for networks that block `.exe` downloads) |
| `GroundPro-rotahub.zip` | The package RotaHub installs: `app\` goes to `%LOCALAPPDATA%\GroundPro` |
| `rotahub.jws` | Tags only: the release manifest RotaHub trusts. It is signed by `packaging/New-HubManifest.ps1` with Rotaidem's release key (the `UPDATE_SIGNING_KEY` secret, as in HeavyPro), which must match `packaging/update-public-key.json` |
| `GroundProFeasibility.zip` | The feasibility test |
| `SHA256SUMS.txt` | Checksums of all of the above |

Code signing works as in HeavyPro: `packaging/Sign-File.ps1` signs `GroundPro.exe` and `GroundProSetup.exe` once the `CODE_SIGNING_PFX_BASE64` and `CODE_SIGNING_PFX_PASSWORD` secrets exist. Until then it only warns.

## Project layout

Same structure as HeavyPro: the physics is a plain library with no SimConnect dependency, so it can be unit-tested anywhere.

```
GroundPro.sln
src/GroundPro.Core         Ground physics, profiles, settings (netstandard2.0)
  Physics/GroundMomentum   breakaway + weight model
  Physics/Brakes           progressive brakes (HeavyPro's toe-brake model + brake keys)
  Physics/Steering         speed-scheduled tiller
  Physics/TaxiEngine       runs all three, ownership and self-checks
  Physics/AcceptanceMonitor whether the aircraft takes a write
  Models/GroundProfiles    per-type ground data
  Services/HeavyProLink    who owns the brakes when HeavyPro runs
  LicenseGate              no writes without a confirmed licence lease
src/GroundPro.Core.Tests   xUnit tests (net8.0): dotnet test src/GroundPro.Core.Tests
src/GroundPro.App          Windows app (WinForms, .NET Framework 4.8): SimLink, MainForm, tray, licence
src/GroundPro.Setup        GroundProSetup.exe: activation, install, update, uninstall
src/Rotaidem.Licensing   Rotaidem's licence library (shared with HeavyPro)
src/Shared               GroundProLicense: product id, key prefix, server (linked into app and setup)
packaging/               Payload, signing and RotaHub manifest scripts; licence and release public keys
tools/FeasibilityTest    The original feasibility test
lib/                     SimConnect runtime from the MSFS 2024 SDK
```

## Feasibility test

`tools/FeasibilityTest` is the automated test that checks whether the sim accepts these effects from outside the aircraft. Its results would settle what GroundPro's self-checks currently decide live. Download [GroundProFeasibility.zip](https://github.com/tornqvisteliaz-cyber/groundPro/releases/download/latest-build/GroundProFeasibility.zip). In MSFS 2024, line the aircraft up on a runway with the engines running, the parking brake released and HeavyPro and GroundPro closed. Open `GroundProFeasibility.exe` and press **Run test**. It takes about two minutes, taxis up to 14 kt several times, and saves its log in Documents\GroundPro.

| Phase | What it does | Tells us |
|---|---|---|
| 1 | Idle, no help | How much the aircraft creeps at idle |
| 2 | Idle, speed zeroed every frame | Whether a hold works |
| 3 | 55 % thrust, held until N1 > 45 % | Whether a breakaway hold beats thrust and releases cleanly |
| 4 | Idle coast, no help | The aircraft's own rolling deceleration |
| 5 | Idle coast with 1.5 ft/s² extra drag written into its speed | Whether rolling resistance can be added |
| 6 | Brakes 50 % through the brake position variables | Whether brakes can be set directly |
| 7 | Brakes 50 % through brake axis events | Whether brakes can be driven like hardware |
