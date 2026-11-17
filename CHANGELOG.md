# TaxiPro changelog

## 0.2.0

TaxiPro becomes a product you install and leave running next to HeavyPro.

- **Installer:** `TaxiProSetup.exe` is one file. It installs TaxiPro into your user folder (no administrator rights), adds Start menu and desktop shortcuts and an entry under Windows' installed apps, and opens TaxiPro. Running it again updates TaxiPro. **Uninstall** removes TaxiPro and keeps your settings. No licence key is needed for now.
- **Tray:** closing the window keeps TaxiPro running in the system tray, where its tooltip shows what it is doing. Right-click the icon to open TaxiPro or exit it. **Start with Windows** starts it in the tray when you log in. Opening TaxiPro again brings back the copy already running.
- **Runs with HeavyPro:** TaxiPro now leaves the toe brakes to HeavyPro exactly when HeavyPro shapes them. It makes the same decision HeavyPro makes, from HeavyPro's settings and the same MSFS control profiles. Before, if the toe brakes were still bound in MSFS, HeavyPro stood aside and so did TaxiPro, and neither shaped the brakes. TaxiPro never touches the stick or the rudder.
- **RotaHub:** every release includes `TaxiPro-rotahub.zip` and, for version tags, a signed `rotahub.jws`, so RotaHub can install and update TaxiPro.

## 0.1.0

First build of TaxiPro, the ground-handling companion to HeavyPro. Not yet flown against MSFS 2024: every effect checks live whether the aircraft accepts it and steps aside if not.

- **Breakaway thrust:** a parked jet with the parking brake released stays put at idle and needs real breakaway thrust (about 30–35 % N1 on a heavy 777) to start rolling. Grass, sand and snow need much more.
- **Weight you can feel:** speed builds at thrust minus rolling resistance over mass, so a heavy jet accelerates slowly, coasts a long way at idle and settles back to rest. Tight turns scrub speed. The **Weight feel** slider sets how strictly (0 = the sim's own, 100 = full physics). TaxiPro only ever takes speed away, never adds it, and hands the speed back to the sim from 25 to 40 kt and at takeoff thrust.
- **Progressive brakes:** toe brakes and the brake keys build pressure smoothly, slower on heavier aircraft, with a deadzone so brakes never stick. Left to HeavyPro when HeavyPro is shaping the toe brakes.
- **Real tiller limits:** full nosewheel authority from the tiller only when slow; above 10 kt it fades to the pedals' 7° on Boeings, and to nothing by 70–80 kt on Airbuses. The nosewheel turns at the steering actuator's rate.
- Ground profiles for the PMDG 777 / 737 / 747, iniBuilds A350-900 / -1000, Fenix A320 / A321, A380, 787, 767, A330, A340, A300/A310, MD-11, TriStar, 757, A220, MD-80, 727, E-Jets, CRJ and 146; any other aircraft by its size class.
- Stays out in the air, in slew, during pushback, with the parking brake set and when rolling backwards.
