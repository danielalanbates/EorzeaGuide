# Third-party data

| Data | Licence | How it is used |
|---|---|---|
| **Questionable** quest paths (`QuestPaths/**/*.json`), github.com/PunishXIV/Questionable | AGPL-3.0 | **Not bundled.** The plugin downloads the repo tarball to the user's own machine when they press *Download quest paths* (or on first run) and reads the step positions as data. No Questionable code is used. |
| **HuntHelper** spawn points (`EorzeaGuide/Data/SpawnPointData.json`), github.com/img02/HuntHelper | MIT | Bundled unmodified; licence in `EorzeaGuide/Data/SpawnPointData.LICENSE-MIT.txt`. |
| **LuminaSupplemental** duty loot CSVs, github.com/Critical-Impact/LuminaSupplemental | GPL-3.0 | Optional local import for personal testing. `tools/import_supplemental_gear.py` generates a JSON index into the local plugin output folder. The source CSVs and derived JSON are gitignored and are not included in public releases. |
| **vnavmesh**, github.com/awgil/ffxiv_navmesh | (none stated) | Called over Dalamud IPC at runtime if the user has it installed. No code copied. |
| Game data (quests, achievements, items, zones...) | Square Enix | Read live from the user's own game install through Dalamud; nothing extracted into this repo. |
