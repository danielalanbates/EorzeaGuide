# The EorzeaGuide library

One SQLite database (`library/eorzeaguide-library.sqlite`, ~19 MB) with everything the guide
knows, plus generated documents. It lives in Drive next to the project and is **not in git**:
it's derived from Square Enix game data and AGPL Questionable data (personal use only).

## Rebuild (about 10 s)

```sh
python3 tools/build_library.py --fetch --csv ~/Downloads/eg-data/csv \
    --quests ~/Downloads/eg-research/questionable.tgz \
    --spawns EorzeaGuide/Data/SpawnPointData.json \
    --out library/eorzeaguide-library.sqlite
python3 tools/verify_library.py library/eorzeaguide-library.sqlite <Questionable checkout> ~/Downloads/eg-data/csv/Aetheryte.csv
python3 tools/export_library.py library/eorzeaguide-library.sqlite library
```

Sources: game sheets as CSV from github.com/xivapi/ffxiv-datamining (`csv/en`, updated each
patch; no game client needed); the Questionable tarball
(`https://codeload.github.com/PunishXIV/Questionable/tar.gz/refs/heads/new-main`); HuntHelper
spawn points (bundled).

## Tables

| Table | What it holds |
|---|---|
| `quests` | every quest: level, expansion, kind (MainScenario/Feature/ClassJob/AlliedSociety/Side/Seasonal/Repeatable), giver + world and map position, turn-in NPC, requirements, whether a hand-mapped step path exists |
| `quest_steps` | per-sequence steps with position and map coordinates; `source` = `questionable` (hand-mapped) or `gamedata` (objective areas + turn-in) |
| `quest_prereqs`, `quest_locks`, `quest_rewards` | the unlock graph and rewards |
| `npc_locations` | first placement of every ENpc/EObj with map coordinates |
| `zones`, `maps`, `expansions` | territories and map scaling (`is_duty = 1` for instanced areas) |
| `aetherytes` | aetherytes and aethernet shards (positions from MapMarker, no height) |
| `vistas` | sightseeing log: position, emote, time window |
| `aether_currents` | field currents (position) and quest currents (quest) |
| `achievements`, `achievement_quests` | every achievement, raw `type`/`key`/`data`, and quests referenced by it |
| `hunt_bills`, `hunt_targets` | every mark bill / clan mark target with kills needed and zone |
| `elite_marks`, `hunt_spawn_points` | S/A/B marks per zone and known spawn points |
| `duties` | every duty with level, item level, and the quest that unlocks it |
| `items`, `item_sources` | every item (`is_gear` for equipment) and every known source: gil shop / exchange (with NPC + location + cost), crafted, quest reward, achievement reward |

Views: `v_zone_checklist`, `v_quest_steps`, `v_msq`, `v_gear`.

## Example queries

```sql
-- Everything left to do in a zone, by what it is
SELECT * FROM v_zone_checklist WHERE name = 'Middle La Noscea';

-- The main scenario in order with where each quest starts
SELECT * FROM v_msq WHERE expansion = 'Shadowbringers';

-- Every step of one quest
SELECT seq, idx, action, target, zone, map_x, map_y, comment FROM v_quest_steps WHERE quest = 'City of the Mord';

-- Best-in-slot candidates for a slot at a level range, with how to get them
SELECT name, item_level, jobs, how_to_get FROM v_gear
WHERE slot = 'Body' AND equip_level BETWEEN 60 AND 70 ORDER BY item_level DESC LIMIT 20;

-- Quests that unlock a duty
SELECT d.name, q.name AS unlock_quest FROM duties d JOIN quests q ON q.id = d.unlock_quest;
```

## Verification (tools/verify_library.py, 2026-09-25)

| Check | Result |
|---|---|
| Aetheryte positions vs Questionable's measured table | 197/197 within 15 yalms |
| Aetheryte coverage (visible, in the Aetheryte sheet) | 0 missing; 29 invisible aethernet exits correctly excluded |
| Quest-giver position (game data) vs Questionable accept step | 4,325/4,325 within 10 yalms |
| Quests with at least one location | 5,370/5,373; 4,327 hand-mapped step paths |
| Main scenario | 1,050 quests (ARR 295, HW 138, SB 162, ShB 157, EW 155, DT 143) |
| Aether currents locatable | 303/303 |
| Sightseeing log | 340 vistas across 60 zones |
| Equipment with a known source | 18,349/29,057 (the rest are duty drops, not in the data) |
