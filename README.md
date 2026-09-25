# EorzeaGuide

A Zygor-style guide for **FINAL FANTASY XIV**, built as a personal-use Dalamud plugin, with a
Completionist's-Guide-style dashboard. It leads you with an on-screen arrow and a drawn
"road" through quests, zones, achievements, hunts, duties and gear.

**Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.**
PolyForm Noncommercial 1.0.0 + 10% commercial rider — see [LICENSE](LICENSE).
Contact: help@batesai.org · https://batesai.org · Third-party data: [THIRD_PARTY.md](THIRD_PARTY.md)

It is a guide, not a bot: it never moves your character, fights, or talks to NPCs. You play;
it points.

## What it covers

| Area | Where the data comes from | Coverage |
|---|---|---|
| Every quest (MSQ, feature, job, allied society, side) | Game Quest sheet + Questionable step paths | All quests listed and trackable. About 4,300 have hand-mapped per-step positions; the rest fall back to the game's own objective areas and turn-in NPC |
| Leveling route | Generated: MSQ chain in prerequisite order + available side/job quests in your zone | Automatic |
| Every zone | TerritoryType, Aetheryte, Adventure, AetherCurrent sheets | Quests starting there, aetherytes, sightseeing vistas, aether currents, elite marks |
| Every achievement | Achievement sheet | All listed with live completion; quest-based ones are guidable |
| Hunts | MobHuntOrder sheets + HuntHelper spawn points | Every bill target with kills; S/A/B spawn-point sweep per zone; exact bill-mob spots learned the first time you see one |
| Duties ("missions") | ContentFinderCondition, game quest unlocks, Questionable duty steps | 774 listed; 39 have a confirmed unlock quest and 243 have a related quest path. Completion is shown where the game exposes it |
| Every piece of equipment | Item + GilShop/SpecialShop/Recipe/quest/achievement rewards | 29,057 items listed; about 18,300 have a known acquisition source in the plugin. Duty drops and other sources still need mapping |

For a **local personal build**, the optional [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental)
duty-loot, relic, store, and item-source import raises known gear sources to 24,811 of 29,057
and identifies 1,455 more as no longer obtainable. The remaining 2,791 still need a source
or status. Its GPL-3.0 data is
kept out of this repository and public releases. See [THIRD_PARTY.md](THIRD_PARTY.md).

## The arrow and the road

- **Waypoint arrow** at the top of the screen (Zygor-style), relative to your camera, with
  distance and the step text. It turns green when you arrive.
- **Road**: with **vnavmesh** installed and ready, the path to the objective is drawn on the
  ground as a ribbon of chevrons along its obstacle-aware path. When no navigable path is
  available, the guide shows a direct bearing and does not draw a road through obstacles.
- **Objective order**: objectives in your current zone use a nearest-neighbour tour improved
  by 2-opt. This is a straight-line-distance heuristic, not a guarantee of the shortest
  walkable tour. Each displayed road leg uses vnavmesh pathfinding.

## Modes

`/eguide` → Guide tab, or a **Guide me** button on any row.

| Mode | What the arrow leads through |
|---|---|
| Leveling | Your accepted quests, the next MSQ, available side/job quests here, then aetherytes, vistas and aether currents here |
| Complete this zone | Everything left in a zone |
| Quest | One quest; if it's locked, it walks back to the first prerequisite you can do |
| Achievement | Quest-linked achievements step by step; others are tracked |
| Hunt | Targets on the bills you hold |
| S/A/B sweep | Every known elite-mark spawn point in the zone |

Commands: `/eguide` (dashboard), `/eguide step` (toggle the step box), `/eguide next`
(skip the current objective), `/eguide quests` (print active quest ids).

## Install (personal dev plugin, XIV on Mac)

1. XIV on Mac → Settings → enable **Dalamud**. Launch the game once.
2. Build (below) or unpack a tested release zip under `~/Downloads/EorzeaGuide-plugin/`.
3. In game: `/xlsettings` → Experimental → Dev Plugin Locations → add
   the `EorzeaGuide.dll` in that folder using its Wine `Z:` path → Save.
4. `/xlplugins` → Dev Tools → Installed Dev Plugins → enable **EorzeaGuide**.
5. Recommended: install **vnavmesh** (from its custom repo) for obstacle-aware roads.
6. The first run downloads the quest paths (about 2 MB). Settings → *Download / update quest
   paths* refreshes them after a patch.

## Build

```sh
cp -R "<your Google Drive Code folder>/EorzeaGuide" ~/Downloads/EorzeaGuide-build
cd ~/Downloads/EorzeaGuide-build/EorzeaGuide
~/.dotnet/dotnet build -c Release
```

After building, a local supplemental gear index can be generated with:

```sh
python3 tools/import_supplemental_gear.py \
  --supplemental "<LuminaSupplemental checkout>/src/LuminaSupplemental.Excel/Generated" \
  --duties "<current game CSV folder>/ContentFinderCondition.csv" \
  --items "<current game CSV folder>/Item.csv" \
  --out EorzeaGuide/bin/Release/Data/SupplementalGearSources.json
```

Run this from the project root after checking out LuminaSupplemental separately under
Downloads. The output is gitignored. Remove it when preparing a public release.

The project references Dalamud from
`~/Library/Application Support/XIV on Mac/dalamud/Hooks/<version>/` (the path is in the
csproj; bump it when Dalamud updates). It targets net10.0-windows and Dalamud API 13.

## Tests (offline, no game client running)

| Test | Command | Checks |
|---|---|---|
| Quest-path parser | `cd Tests/PathsTest && dotnet run -c Release -- questionable.tgz` | Parses the real Questionable tarball the same way the plugin does |
| Game database | `cd Tests/DbTest && dotnet run -c Release -- <sqpack dir> questionable.tgz ./plugindir` | Builds the full GameDb from the real game files through Lumina, printing coverage counts and a coordinate sanity check |

See [docs/HANDOFF.md](docs/HANDOFF.md) for architecture, verified status, known gaps and next steps.
