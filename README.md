# TaxiPro

Ground handling for Microsoft Flight Simulator 2024, by Rotaidem. A heavy jet should feel heavy on the ground too:

- **Breakaway thrust:** a parked 300-tonne jet does not creep at idle. It needs a push of thrust to start rolling, then settles back.
- **Weight you can feel:** heavy jets build speed slowly, coast, and take time to stop, scaled by their real weight.
- **Real steering limits:** full nosewheel authority from the tiller only when slow, a few degrees from the rudder pedals at speed.
- **Progressive brakes:** smooth, weight-aware braking that never sticks.

TaxiPro sits next to [HeavyPro](https://github.com/tornqvisteliaz-cyber/heavypro) and installs through RotaHub.

## Status: feasibility test

Before building the product we check whether the sim, and the study-level aircraft in particular (PMDG 777, iniBuilds A350), accept these ground effects from outside the aircraft. `tools/FeasibilityTest` does that automatically and logs the result.

### Running the test

1. Download [TaxiProFeasibility.zip](https://github.com/tornqvisteliaz-cyber/taxiPro/releases/download/latest-build/TaxiProFeasibility.zip), built from `main` by GitHub Actions, and unzip it. Or build it yourself: `dotnet build tools/FeasibilityTest -c Release`.
2. In MSFS 2024: the aircraft lined up on a runway, engines running, parking brake released, hands off the throttles, HeavyPro closed.
3. Open `TaxiProFeasibility.exe`. The window connects to MSFS by itself and shows the aircraft, weight, parking brake, speed and N1. Press **Run test** and confirm. It takes about two minutes, taxis up to 14 kt several times, and stops itself; **Stop** brings the aircraft to a halt at any time. Results show in the window; logs are saved in Documents\TaxiPro.

The test, in order:

| Phase | What it does | Tells us |
|---|---|---|
| 1 | Idle, no help | How much the aircraft creeps at idle |
| 2 | Idle, speed zeroed every frame | Whether a hold works |
| 3 | 55 % thrust, held until N1 > 45 % | Whether a breakaway hold beats thrust and releases cleanly |
| 4 | Idle coast, no help | The aircraft's own rolling deceleration |
| 5 | Idle coast with 1.5 ft/s² extra drag written into its speed | Whether rolling resistance can be added |
| 6 | Brakes 50 % through the brake position variables | Whether brakes can be set directly |
| 7 | Brakes 50 % through brake axis events | Whether brakes can be driven like hardware |

The summary at the end, also written to `taxipro-feasibility-*.log`, compares the phases.
