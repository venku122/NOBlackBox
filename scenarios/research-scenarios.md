# Research Session Scenarios

Run these on Windows with BepInEx logging enabled and Research flags toggled as needed.

## Common setup
- Copy built `NOBlackBox.dll` to `BepInEx/plugins/`
- Enable `ResearchLoggingEnabled=true` in config
- Enable per-category flags as needed
- Collect from `BepInEx/plugins/NOBlackBox/research-dumps/`
- Collect from `BepInEx/LogOutput.log`

## Scenario matrix

| # | Name | Flags | What to observe |
|---|------|-------|-----------------|
| S1 | Basic spawn/takeoff/land/exit | (none) | Auto-start fires, REC shows, .zip.acmi appears, no partial files |
| S2 | Aircraft locks 1 target | `ResearchDumpTargets=true` | `LockedTarget` present in ACMI for single lock |
| S3 | Aircraft locks multiple targets | `ResearchDumpTargets=true` | `LockedTarget`, `LockedTarget1`, ... all present |
| S4 | Ground AA locks 1 aircraft | `ResearchDumpTargets=true` | Ground vehicle emits `LockedTarget` |
| S5 | Ground AA sees multiple targets | `ResearchDumpTargets=true` | `LockedTarget1`, `LockedTarget2` emitted |
| S6 | Medusa laser fires at valid target | `ResearchDumpApiFields=true` | Identify laser class, firing state, target ID |
| S7 | Medusa laser has no target | `ResearchDumpApiFields=true` | `targetID` = -1 behavior |
| S8 | Aircraft jammer toggled | `ResearchDumpEW=true` | Find jammer field, active/inactive states |
| S9 | Medusa jamming pod active | `ResearchDumpEW=true` | Find pod state, target/affected unit |
| S10 | Radar/datalink detection transition | `ResearchDumpDetection=true` | Detection state per unit per faction |
| S11 | City area observed before/after destruction | `ResearchDumpBuildings=true` | Count buildings vs scenery, networked vs not |
| S12 | Mission restart / exit-to-menu lifecycle | (none) | Check `Close()` called once, no stale files |
