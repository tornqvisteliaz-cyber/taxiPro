# TaxiPro changelog

## 0.1.0

First build of TaxiPro, the ground-handling companion to HeavyPro. Not yet flown against MSFS 2024: every effect checks live whether the aircraft accepts it and steps aside if not.

- **Breakaway thrust:** a parked jet with the parking brake released stays put at idle and needs real breakaway thrust (about 30–35 % N1 on a heavy 777) to start rolling. Grass, sand and snow need much more.
- **Weight you can feel:** speed builds at thrust minus rolling resistance over mass, so a heavy jet accelerates slowly, coasts a long way at idle and settles back to rest. Tight turns scrub speed. The **Weight feel** slider sets how strictly (0 = the sim's own, 100 = full physics). TaxiPro only ever takes speed away, never adds it, and hands the speed back to the sim from 25 to 40 kt and at takeoff thrust.
- **Progressive brakes:** toe brakes and the brake keys build pressure smoothly, slower on heavier aircraft, with a deadzone so brakes never stick. Left to HeavyPro when HeavyPro is shaping the toe brakes.
- **Real tiller limits:** full nosewheel authority from the tiller only when slow; above 10 kt it fades to the pedals' 7° on Boeings, and to nothing by 70–80 kt on Airbuses. The nosewheel turns at the steering actuator's rate.
- Ground profiles for the PMDG 777 / 737 / 747, iniBuilds A350-900 / -1000, Fenix A320 / A321, A380, 787, 767, A330, A340, A300/A310, MD-11, TriStar, 757, A220, MD-80, 727, E-Jets, CRJ and 146; any other aircraft by its size class.
- Stays out in the air, in slew, during pushback, with the parking brake set and when rolling backwards.
