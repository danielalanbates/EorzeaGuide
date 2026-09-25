# EorzeaGuide — handoff notes for the next session / AI

Read this before touching the code. Keep it current.

## Goal (Daniel, 2026-09-25)
A Zygor equivalent for FFXIV, modelled on the Completionist's Guide: every quest, mission,
zone, achievement and monster hunt; a gear section covering every piece of equipment; and a
most-efficient, unobstructed "road" through each zone with an arrow to follow. Personal use
only. It must never automate play (no movement, combat or dialogue); it only draws guidance.

## Architecture

```
Plugin.cs            services, framework tick (planner -> navigator -> map flag), commands
Configuration.cs     all settings + guide mode/focus + skipped objectives
Data/
  GameDb.cs          builds every index from the game's Excel sheets (background thread, ~2s)
  QuestPathStore.cs  downloads + parses Questionable QuestPaths tarball -> questpaths-index.json
  Progress.cs        read-only game state: quests, currents, vistas, aetherytes, achievements, hunts, inventory
  LearnedPositions   remembers where hunt-bill monsters were seen (learned-positions.json)
  MapMath.cs         world <-> map-coordinate conversion
  Models.cs          QuestInfo, ZoneInfo, AchievementInfo, HuntTarget, DutyInfo, GearItem
Planning/Planner.cs  the "Zygor brain": mode -> ordered Objective queue (route-optimised per zone)
Nav/Navigator.cs     vnavmesh IPC pathfinding (Nav.Pathfind -> Task<List<Vector3>>), straight-line fallback
Nav/RouteOptimizer   nearest-neighbour + 2-opt open tour from the player
Nav/Overlay.cs       road chevrons, destination beacon, camera-relative waypoint arrow
Windows/StepWindow   small always-on box: current objective, next 4, Map/Teleport/Skip
Windows/MainWindow   dashboard tabs: Guide, Overview, Zones, Quests, Achievements, Hunts, Duties, Gear, Custom, Settings
GuideEngine.cs       legacy hand-written JSON guides (Custom tab)
Tests/PathsTest      offline: Questionable tarball parser
Tests/DbTest         offline: full GameDb build against real sqpack via Lumina (test shim for IDataManager)
```

### Where each coverage claim comes from
- Quest steps: Questionable per-sequence steps (exact 3D positions, NPC ids) when present;
  otherwise the game's `Quest.TodoParams[].ToDoLocation` (Level rows = objective areas) and
  the `TargetEnd` NPC's placement for the turn-in.
- Quest giver: `Quest.IssuerLocation` (Level row).
- NPC/EObj placements: first `Level` row whose `Object` is that id.
- Aether currents: EObj whose `Data` is the AetherCurrent row, located through Level; quest
  currents map to their quest.
- Vistas: `Adventure.Level`; completion index = RowId - 2162688.
- Hunts: `MobHuntOrderType` -> `MobHuntOrder` subrows; held bill = `MobHunt.GetObtainedHuntOrderRowId(markIndex)`,
  kills = `GetKillCount(markIndex, mobIndex)`. Elite spawn points come from HuntHelper (map coordinates, Y snapped via vnavmesh).
- Gear sources: GilShopItem, SpecialShop, Recipe, quest rewards, achievement rewards; vendor
  location through `ENpcBase.ENpcData` -> shop id.

## Status (update this table)

| Item | Status | Evidence |
|---|---|---|
| Plugin compiles (net10.0-windows, Dalamud API 13, Dalamud 15.0.3.5 refs) | ✅ | `dotnet build -c Release` 0 errors, 0 warnings on 2026-09-25 |
| Questionable parser | ✅ | PathsTest: 4,327 quests, 29,759 steps (27,807 with positions), 0.4 s |
| GameDb build on real game files | ✅ | DbTest: 5,373 quests, 300 zones, 4,003 achievements, 1,272 hunt targets, 774 duties; 4,325/4,325 quest givers within 10y; 106/106 available aetheryte X/Z positions within 15y |
| Library (SQLite + zone/MSQ docs + CSVs) | ✅ | `tools/verify_library.py`: 8/8 checks pass (docs/LIBRARY.md) |
| Aetheryte positions | ✅ fixed | Were read from Aetheryte.Level (rows not in the Level sheet); now MapMarker, 197/197 within 15y |
| Virtual player (Tests/VPlayer) | ✅ offline run | Starting classes 26 (Limsa), 1 (Ul'dah), and 4 (Gridania): 996/998/997 MSQ completed respectively, 0 available MSQ left, 0 stalls each. Zone run: 143 zones, 0 objectives left, 0 stalls. See limitations below |
| Runs in game | ❌ NOT YET VERIFIED | Dalamud has never been enabled in XIV on Mac on this Mac; the client was still downloading on 2026-09-25 |

2026-09-25 follow-up: the ground road now draws only for a successful vnavmesh path.
The direct-bearing arrow remains available when vnavmesh has no path. The zone objective
order still uses straight-line distances and is not a proven shortest walkable tour.

2026-09-25 simulation follow-up: `Planner` now excludes quests marked repeatable from
leveling and zone sweeps even when the game's category is Feature. The virtual player seeds
the correct city-arrival quest for a starting class; those quests are classified Side in
the game data and gate the first MSQ. Its missed-MSQ check excludes mutually exclusive
starting-city and Grand Company branches. DbTest compares map-marker aetherytes by X/Z
because their height is unavailable until runtime floor snapping.

The virtual player simulates quest acceptance/completion and collectible progress; it does
not test combat, duty clears, weather/time windows, navmesh obstacles, arrow orientation,
cross-zone travel, or the live game's UI and API behavior. Its zero-stall result is an
offline planner result, not an in-game completion guarantee.

Gear with no indexed vendor, recipe, quest, or achievement source now says "Source not
indexed". Previously the plugin labeled every such item as a possible duty drop, which
was unsupported for many items. The missing-source count is still about 10,700 items.

## Known gaps and next steps (in priority order)
0. **Expand virtual-player scenarios** to all starting classes and Grand Companies, plus
   hunt, duty, achievement, and gear guidance. One class from each starting city is clean.
1. **First in-game run.** Enable Dalamud, add the dev plugin, and check each piece in turn:
   the step window appears; `/eguide quests` prints ids; the arrow points the right way when
   the camera rotates; the road follows vnavmesh; the map flag lands on the objective.
2. **Arrow direction** is computed by projecting the player position and a point 3 yalms
   toward the target to screen space. If that proves unstable at steep camera angles, switch
   to the camera yaw from `CameraManager.Instance()->CurrentCamera`.
3. **Achievement criteria**: only `Type == 6` (quest) achievements are linked to quests. Map
   the other types from `Achievement.Key`/`Data` (counts, levels, duties, other achievements)
   to make more of them guidable.
4. **Quest classification** uses JournalSection/JournalCategory name strings; verify on the
   current client (Main Scenario / Chronicles / Class & Job / Allied Society).
5. **Hunt-bill monster positions** are learned on sight. A better source: BNpc spawn data
   (Mappy-style datasets) if one with an open licence exists.
6. **Duty drops** for gear are not in the Excel data. Candidate: Garland Tools data (check
   its licence first) or Teamcraft's open data.
7. **Cross-zone routing** only offers a teleport to the nearest unlocked aetheryte; aethernet
   and airship/ferry legs aren't modelled.
8. **Road across zones / flying**: `Objective.Fly` is passed to vnavmesh from Questionable's
   step flag only.

## Rules that bit before
- Source of truth: Google Drive `My Drive/Code/EorzeaGuide` (moved from iCloud 2026-09-25). Build from `~/Downloads/EorzeaGuide-build`, never inside a cloud-synced folder.
- `~/Library/Application Support/XIV on Mac` is a symlink to `/Volumes/x10/Video Games/Mac/XIV on Mac Support`
  (the internal disk can't hold the client). The launcher sometimes fails a patch download
  ("could not download ... after 3 attempts"). Fix: curl that exact patch URL from the log into
  `.../XIV on Mac Support/patch/game/4e9a232b/` and reopen the launcher.
- The launcher downloads each patch into the macOS user temp dir (`getconf DARWIN_USER_TEMP_DIR`,
  on the INTERNAL disk) before moving it to x10. Each kill/crash leaves a 0.2-1.5 GB orphan
  there (4.2 GB found on 2026-09-25). Delete orphans only after `lsof` shows no process has them open.
- Lumina sheet structs are generated for the current game version; running DbTest against a
  partly patched client gives garbage fields (seen: 4,098 "seasonal" quests, 56 quest givers).
