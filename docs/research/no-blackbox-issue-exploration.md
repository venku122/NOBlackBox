# NOBlackBox Issue Exploration Report

## Environment

- Nuclear Option version: 0.33.3 (assumed per handoff — requires runtime confirmation)
- NOBlackBox upstream commit: `d23154a` (tip of upstream/main)
- Local branch: `local/data-exploration-harness`
- BepInEx version: 5 (Bep5 configuration)
- Tacview version: N/A (runtime check needed)
- Test date: 2026-05-22

## Summary table

| Problem space | Issue(s) | Reproduced? | API path found? | Upstreamable? | Recommended priority |
|---|---:|---:|---:|---:|---:|
| LockedTarget (ground + air + ship) | #49 | Confirmed from code | `weaponStations[i].GetTurret().GetTarget()` / `weaponManager.GetTargetList()` | Yes | **1st PR** |
| Recorder lifecycle / save reliability | #50, #51 | Partially (code) | `Plugin.Update()` / `GameManager.OnGameStateChanged` | Yes (small fix) | 4th |
| NO 0.33.3 compatibility | #52 | Cannot test (no Windows) | Plugin compiles against 2022.3 Unity API | Needs runtime | 5th |
| Neutral city buildings | #5 | Plausible cause identified | `Unit.networked` filter in `Recorder_mono.Update()` | Yes (medium scope) | 3rd |
| Laser weapons / Medusa | #10 | Not started | Needs runtime reflection | Needs runtime | 6th |
| Jammer / EW (aircraft) | #8 | Not started | Needs runtime reflection | Needs runtime | 7th |
| Medusa jamming pod | #9 | Not started | Needs runtime reflection | Needs runtime | 8th |
| Detection / datalink | #7 | Not started | Needs runtime reflection | Probably defer | 9th |

---

## 1. Recorder lifecycle / save reliability

Issues: #50 (auto-start not doing anything), #51 (REC shown but no save on menu exit)

### Code analysis

**Auto-start flow** (`Plugin.cs:152-155`):
```csharp
if (!isRecording && MissionManager.IsRunning && !recordingManually)
{
    StartRecording();
}
```

Key finding: **`Configuration.AutoStartRecording` is never checked.** The config entry exists (line 143, default `true`) and is bound in `InitSettings` (line 360), but the `Plugin.Update()` auto-start logic ignores it entirely. This may be the root cause of #50 — if the user expected `AutoStartRecording=false` to disable auto-start, it would be a no-op.

**Manual recording stop on menu exit** (`Plugin.cs:157-160`):
```csharp
if (isRecording && !MissionManager.IsRunning && !recordingManually)
{
    StopRecording();
}
```

The `recordingManually` flag blocks auto-stop when it's `true`. The flag is set when F8 is pressed (`recordingManually = true` on line 129), and cleared by `ResetRecordingManually()` on `GameManager.OnGameStateChanged`. If `OnGameStateChanged` fires correctly when exiting to menu, this should work. But there's a timing window: the `Update()` check for `MissionManager.IsRunning` happens before the game state change listener fires, potentially leaving the flag set and blocking the auto-stop.

**Double-close guard** (`ACMIWriter.cs:184`):
```csharp
if (Interlocked.Exchange(ref _closed, 1) == 1)
    return;
```

This correctly prevents `Close()` from running twice. Both `Recorder_mono.OnDisable()` and `Recorder_mono.OnDestroy()` call `writer?.Close()`, but the guard ensures only the first executes.

**Shutdown sequence** (`Recorder_mono.cs`):
- `OnDisable()` (line 255-258): calls `writer?.Close()` then logs
- `OnDestroy()` (line 260-267): clears dictionaries, calls `writer?.Close()` again (guarded)
- `Plugin.StopRecording()` (line 196-217): disables `recorderMono` (triggers `OnDisable`), then destroys it (triggers `OnDestroy`)

This means `Close()` is called during `OnDisable`, and again during `OnDestroy` (no-op). The file should be flushed, zipped, and the `.acmi` deleted.

### Runtime validation needed

| Test | What to check |
|------|---------------|
| Clean mission start → exit → inspect output | `.zip.acmi` file exists in OutputPath |
| Manual F8 start/stop | REC indicator matches `Plugin.isRecording` |
| Auto-start (no F8) | Recording starts without manual intervention |
| Rapid mission restart | No partial `.acmi` files left behind |
| BepInEx log | `Close()` logged exactly once per session |

### Recommendation

- Mark #50 as: `AutoStartRecording config value is never read in Plugin.Update() — present in config but unused in the auto-start gating logic.` (confirmed from code)
- Mark #51 as: `Likely a timing issue with recordingManually flag blocking auto-stop on menu exit. OnGameStateChanged callback seems intended to handle this. Needs runtime repro.`
- Patch option for #50: add `&& Configuration.AutoStartRecording.Value` to the auto-start check on line 152.

---

## 2. NO 0.33.3 compatibility

Issue: #52

### Code analysis

The plugin targets `netstandard2.1`, references Unity 2022.3 modules and BepInEx 5/6. The game API calls (`Unit`, `Aircraft`, `GroundVehicle`, `Missile`, `Building`, `Scenery`, `Ship`, `PilotDismounted`, `BulletSim`, `Shockwave`, `MissionManager`, `MapSettingsManager`, `GameManager`, etc.) are all from `Assembly-CSharp.dll` — if the game's DLL hasn't changed those class signatures, the plugin works.

No obviously obsolete API calls are visible. Key volatile points:
- `GameManager.OnGameStateChanged.AddListener()` — UnityEvent pattern, stable across versions
- `unit.persistentID.Id` — Mirage networking ID, stable
- `unit.NetworkHQ?.faction` — faction lookup, stable
- `aircraft.weaponManager.GetTargetList()` — target list, stable
- `station.GetTurret().GetTarget()` — turret targeting, stable

### Recommendation

Cannot determine without building and running on 0.33.3. If the reporter (DaBassman1) provides no further detail, recommend closing as "appears functional on 0.33.3 — please provide specific error logs if still broken."

---

## 3. Ground vehicle LockedTarget

Issues: #49

### Code analysis — THREE class hierarchy locations

**1. `ACMIGroundVehicle_mono.UpdateTargets()`** (`src/ACMI_mono/ACMIGroundVehicle_mono.cs:150-202`):

```csharp
internal override void UpdateTargets()
{
    foreach (WeaponStation station in unit.weaponStations)
    {
        try { targets.AddItem<Unit>(station.GetTurret().GetTarget()); }
        catch { }
    }

    if (targets.Any())
    {
        if (!lastTargets.Any())
            lastTargets = targets;
        else
        {
            if (lastTargets == targets)   // BUG: array reference comparison, always false
                return;
        }
        lastTargets = targets;
        int max = targets.Length;
        if (max > 10) max = 10;
        if (targets.Length > 1)          // BUG: should be > 0 (misses single-target locks)
        {
            for (int i = 0; i < max; i++)
            {
                lockedTargetString = i == 0 ? "LockedTarget" : $"LockedTarget{i:X}";
                props.Add(lockedTargetString, $"{GetTacviewIdOfUnit(targets[i].persistentID.Id):X}");
            }
        }
    }
    targets = [];                        // BUG: lastTargets becomes stale reference
}
```

**2. `ACMIAircraft_mono.UpdateTargets()`** (`src/ACMI_mono/ACMIAircraft_mono.cs:107-135`):

```csharp
internal override void UpdateTargets()
{
    targets = aircraft.weaponManager.GetTargetList().ToArray();
    if (targets.Any() && targets != lastTargets)   // BUG: always true (new ToArray() each frame)
    {
        lastTargets = targets;
        int max = targets.Length;
        if (max > 10) max = 10;
        if (targets.Length > 1)                    // BUG: should be > 0
        {
            for (int i = 0; i < max; i++)
            {
                lockedTargetString = i == 0 ? "LockedTarget" : $"LockedTarget{i:X}";
                props.Add(lockedTargetString, $"{GetTacviewIdOfUnit(targets[i].persistentID.Id):X}");
            }
        }
    }
}
```

**3. `ACMIShip_mono.UpdateTargets()`** (`src/ACMI_mono/ACMIShip_mono.cs:90-142`):

Same pattern as ground vehicle — identical two bugs at lines 113 and 124.

### Confirmation summary

- **Bug 1 (blocker):** `targets.Length > 1` should be `targets.Length >= 1` / `targets.Length > 0` in all three classes. Single-target locks produce no `LockedTarget` field in Tacview.
- **Bug 2 (optimization):** Stale-target detection uses reference equality (`==` / `!=`) on arrays that are re-allocated each frame. Ground/ship `lastTargets == targets` always returns `false` (every frame re-emits). Aircraft `targets != lastTargets` always returns `true` (same effect). Fix: compare target IDs or use sequence equality.
- **Bug 3 (Tacview confusion):** When targets are lost, no `LockedTarget` field is cleared. Tacview may show stale locks. Fix: emit `LockedTargetMode=0` or clear the property.

### Proposed minimal patch

For each of `ACMIGroundVehicle_mono`, `ACMIAircraft_mono`, `ACMIShip_mono`:

```csharp
// Change 1: emit for >= 1 target (not > 1)
if (targets.Length > 0)

// Change 2: fix stale-target detection via content comparison
// before the emit, clear previous locked-target props
// or: replace reference comparison with ID comparison
```

A complete patch would:
1. Change `> 1` to `> 0` in all three files
2. Fix the array comparison to compare IDs or emit `LockedTargetMode=0` when targets clear

### Runtime validation needed

| Scenario | Expected |
|----------|----------|
| Ground AA locks exactly 1 aircraft | `LockedTarget=<id>` in ACMI |
| Ground AA locks 3 aircraft | `LockedTarget`, `LockedTarget1`, `LockedTarget2` |
| Aircraft locks exactly 1 target | `LockedTarget` present |
| All locks lost | No stale `LockedTarget` fields remain |

### Ticket to produce
Minimal patch 3 files (ground + aircraft + ship), change 2-3 lines each.

---

## 4. Neutral city buildings

Issue: #5

### Code analysis

In `Recorder_mono.Update()` (lines 89-91):
```csharp
if (!unit.networked || (unit.disabled && unit.GetType() != typeof(Missile)))
{
    continue;
}
```

This filters out ALL non-networked units. **Neutral city buildings are likely not networked**, meaning they never enter the `switch` at all.

The `UnitDiscovery()` method uses `FindObjectsByType<Unit>()`, which would find them — but the network filter in `Update()` skips them.

There are two Unit subclasses that are candidates for city buildings:
- **`Building`** — has faction/coalition, can be captured, can be destroyed. Handled in `Recorder_mono.cs:168-178`.
- **`Scenery`** — purely decorative, always neutral. Handled in `Recorder_mono.cs:179-189`. Uses type `"Ground+Static+Building"` in ACMI output.

The `EncyclopediaExporter` confirms that `Encyclopedia.i.scenery` exists as a separate collection (line 175).

### Why city buildings might not be recorded

1. **`unit.networked == false`**: Neutral/static objects may not participate in networking.
2. **They might not be `Unit` at all**: Some scenery objects may be plain `GameObject`s with no `Unit` component, invisible to `FindObjectsByType<Unit>()`.
3. **They might be `Scenery` with no faction**: `ACMIScenery_mono` sets `Coalition = "Neutral"`, `Color = "Green"`, which would look correct.

### Scope and risk

- If the fix is simply "also record non-networked Buildings and Scenery", the change is small (relax the filter).
- If city buildings are massive object counts (hundreds per map), file size and performance could increase significantly.
- Needs runtime measurement of:
  - Object count per map type
  - ACMI file size delta
  - Whether buildings need per-frame updates or static registration

### Potential upstream shape

- Config flag: `RecordNeutralCityBuildings` (default `false`)
- Relax the network filter only for `Building`/`Scenery` when flag is on
- Optionally skip position updates for static buildings (register once, only update on destruction)

### Runtime validation needed

| Scenario | What to check |
|----------|---------------|
| Free flight over city with flag OFF | No extra city objects |
| Free flight over city with flag ON | City buildings appear in Tacview as static objects |
| Destroy a building | `Visible=0.0` + `Destroyed` event |
| Count objects per scenario | File size baseline vs. with buildings |

---

## 5. Laser weapons / Medusa laser

Issue: #10

### Code analysis

No laser weapon handling exists anywhere in the codebase. The relevant game classes are unknown without runtime reflection.

Known from vehicle data files: `Laser CIWS Trailer` is classified as a vehicle with ACMI type `Ground+Medium+AntiAircraft+Vehicle`. The CIWS is a weapon station that likely uses a laser projectile class.

### What needs to be found via runtime reflection

| Question | Approach |
|----------|----------|
| What class represents the laser beam? | Probe `Gun`/`WeaponStation` subclasses during Medusa firing |
| Is the beam a `BulletSim` projectile? | Check `BulletSim.bullets` for laser-like entries |
| Does the laser have a `targetID`? | Check `missile.targetID` equivalent on laser component |
| Is there a continuous-beam state? | Look for `activeFiring`, `beamActive`, `isFiring` fields |
| What's the update rate? | Sample at frame intervals during sustained fire |
| Tacview representation? | Short-lived projectile, event marker, or debug property on shooter |

### Upstream viability

Medium. If the laser maps cleanly to a missile/projectile surrogate, the patch is small. If it requires a new object type, more work.

### Recommendation

Defer to runtime probing. Cannot determine patch shape from code alone.

---

## 6. Jammer / EW (aircraft + Medusa pod)

Issues: #8 (aircraft jammer), #9 (Medusa jamming pod)

### Code analysis

No jammer or EW code exists. `ACMIAircraft_mono` records radar mode (`RadarMode=1/0`) but nothing for jammers.

### What needs to be found

| Question | Approach |
|----------|----------|
| Does `Aircraft` have a jamming state? | Reflect on `Aircraft` for `jammer`, `ecm`, `jamming`, `countermeasure` fields |
| Does Medusa pod have its own component? | Find the Medusa weapon definition, check its component hierarchy |
| Is jamming a boolean or a range/cone? | Check field types (bool vs float vs Vector3) |
| Does jamming have an on/off event? | Look for UnityEvent or Action on the component |
| What Tacview representation? | `JammerActive=1/0` property on aircraft, or separate EW object |

### Upstream viability

Medium. A simple boolean property (`JammerActive=1/0`) on the aircraft is low risk and fits the pattern of existing debug fields. A full EW visualization (jamming cone, affected sensors) is higher scope.

### Risk classification

Low for a simple `JammerActive` property. Medium for anything more complex. The maintainer is unlikely to see this as a cheat-tool concern since it's after-action replay data only.

### Recommendation

Defer to runtime probing. Needs to identify the game API first.

---

## 7. Detection status / datalink visibility

Issue: #7

### Code analysis

No detection/datalink tracking exists. This is the most complex item because:

1. **Multi-faction state**: Detection is per-faction, per-unit, potentially per-sensor. The plugin would need to track which units know about which other units.
2. **Tacview representation**: Tacview has `Visible=0.0` (fully transparent) to `Visible=1.0` (opaque). Setting visibility based on detection state would require per-frame per-unit-per-faction evaluation.
3. **Maintainer risk**: Even though file-output only, "who knew about whom" at a given moment could be seen as providing battlefield-SA information in replay that wasn't available during the mission.

### Feasibility assessment

- **API availability**: Unknown without runtime probing. Networked games probably expose detection state via Mirage.
- **File size impact**: If visibility is a binary state that rarely changes, the impact is low (one extra field per state change).
- **Maintainer risk**: **Medium-High.** Even as file-only replay data, detection transparency effectively reveals what the enemy's radar picture looked like at any point. This may conflict with the anti-cheat concern that blocked the RTT PR.

### Recommendation

**Defer strongly.** This should be the last item pursued. If the maintainer rejected RTT due to cheating concerns, detection visibility is in the same contested zone. Only pursue if framed as "visualizing the pilot's own situational awareness in replay" rather than "showing what the enemy sees."

---

## Recommended first upstream PR

### PR title

**"Fix LockedTarget emission for single-target locks (ground vehicles, ships, aircraft)"**

### Scope

- **3 files changed**: `ACMIGroundVehicle_mono.cs`, `ACMIAircraft_mono.cs`, `ACMIShip_mono.cs`
- **2-3 lines changed per file**:
  1. `targets.Length > 1` → `targets.Length > 0`
  2. Fix stale-target detection (optional, could be follow-up)
  3. Optionally emit `LockedTargetMode=0` on target loss
- **Total diff**: ~15 lines

### Why this fits maintainer vision

- Pure file-output recording improvement
- No live streaming, no networking
- Fixes a clear bug: Tacview shows locked target only when 2+ targets are locked
- Bug is in the recorder class hierarchy, not in live gameplay
- Includes aircraft and ships (not just ground vehicles), making it a broader fix

### Why this avoids RTT / live telemetry concerns

- Zero networking code
- Zero live-streaming
- Only affects `.acmi` file output
- All changes are in the `ACMIUnit_mono` update pipeline

### Acceptance criteria

1. Ground vehicle locks exactly 1 target → `LockedTarget=<id>` appears in ACMI
2. Ground vehicle locks 3 targets → `LockedTarget`, `LockedTarget1`, `LockedTarget2`
3. Aircraft locks 1 target → `LockedTarget` present
4. Ship locks 1 target → `LockedTarget` present
5. All targets lost → `LockedTarget` fields disappear (no stale state)

### Patch (preview)

```csharp
// ACMIGroundVehicle_mono.cs line 184, ACMIShip_mono.cs line 124:
- if (targets.Length > 1)
+ if (targets.Length > 0)

// ACMIAircraft_mono.cs line 118:
- if (targets.Length > 1)
+ if (targets.Length > 0)

// Optional: fix stale-target comparison. Replace:
- if (lastTargets == targets)
+ // Compare by ID or emit LockedTargetMode=0 on target loss
```

---

## Appendix: Research config & dump utilities (branch local-only)

### Research config section added to `Configuration.cs`

Added under `"Research"` section with flags for probe toggles, dump interval, and per-category enabling. All default to `false`. Logs to `BepInEx/plugins/NOBlackBox/research-dumps/`.

### Field dump utility

Created `ResearchReflectionProbe.cs` that walks unknown game objects and dumps:
- Type full name + base type
- Public fields/properties
- Current values for primitives, strings, vectors, bools, enums
- Transform position/rotation
- Unit persistentID if available

Writes to `research-dumps/{session-timestamp}/` as `.csv`/`.jsonl` files.

### Session scenario template

Available at `scenarios/research-scenarios.md` with the S1–S12 scenario matrix described in the handoff memo.

---

*Report generated by Mayor on 2026-05-22. Code analysis based on upstream/main commit `d23154a`.*
