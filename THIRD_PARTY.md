# Third-party data

| Data | Licence | How it is used |
|---|---|---|
| **Questionable** quest paths (`QuestPaths/**/*.json`), github.com/PunishXIV/Questionable | AGPL-3.0 | **Not bundled.** The plugin downloads the repo tarball to the user's own machine when they press *Download quest paths* (or on first run) and reads the step positions as data. No Questionable code is used. |
| **HuntHelper** spawn points (`EorzeaGuide/Data/SpawnPointData.json`), github.com/img02/HuntHelper | MIT | Bundled unmodified; licence in `EorzeaGuide/Data/SpawnPointData.LICENSE-MIT.txt`. |
| **vnavmesh**, github.com/awgil/ffxiv_navmesh | (none stated) | Called over Dalamud IPC at runtime if the user has it installed. No code copied. |
| Game data (quests, achievements, items, zones...) | Square Enix | Read live from the user's own game install through Dalamud; nothing extracted into this repo. |
