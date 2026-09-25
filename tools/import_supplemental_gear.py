#!/usr/bin/env python3
"""Make a local, gitignored gear-source index from LuminaSupplemental CSVs.

The input data is GPL-3.0 licensed by Critical-Impact. This tool does not copy it
into the EorzeaGuide repository or public releases. See THIRD_PARTY.md.
"""

import argparse
import csv
import json
from collections import defaultdict
from pathlib import Path


def rows(path):
    with path.open(newline="", encoding="utf-8-sig") as stream:
        yield from csv.DictReader(stream)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--supplemental", type=Path, required=True,
                        help="LuminaSupplemental/src/LuminaSupplemental.Excel/Generated")
    parser.add_argument("--duties", type=Path, required=True,
                        help="Current game ContentFinderCondition.csv")
    parser.add_argument("--items", type=Path, required=True,
                        help="Current game Item.csv")
    parser.add_argument("--out", type=Path, required=True,
                        help="Local Data/SupplementalGearSources.json (gitignored)")
    args = parser.parse_args()

    names = {int(row["#"]): row["Name"].strip()
             for row in rows(args.duties) if row["#"].isdigit() and row["Name"].strip()}
    item_names = {int(row["#"]): row["Name"].strip()
                  for row in rows(args.items) if row["#"].isdigit() and row["Name"].strip()}
    chests = {int(row["RowId"]): int(row["ContentFinderConditionId"])
              for row in rows(args.supplemental / "DungeonChest.csv")}
    sources = defaultdict(set)

    def add(item, duty, kind):
        if not item or duty not in names:
            return
        sources[item].add(f"{kind}: {names[duty]}")

    for row in rows(args.supplemental / "DungeonDrop.csv"):
        add(int(row["ItemId"]), int(row["ContentFinderConditionId"]), "Duty drop")
    for row in rows(args.supplemental / "DungeonBossDrop.csv"):
        add(int(row["ItemId"]), int(row["ContentFinderConditionId"]), "Duty boss drop")
    for row in rows(args.supplemental / "DungeonBossChest.csv"):
        add(int(row["ItemId"]), int(row["ContentFinderConditionId"]), "Duty boss chest")
    for row in rows(args.supplemental / "DungeonChestItem.csv"):
        add(int(row["ItemId"]), chests.get(int(row["ChestId"]), 0), "Duty chest")

    relic_quests = {}
    for kind, filename in (("Relic weapon", "RelicWeapon.csv"),
                           ("Relic tool", "RelicTool.csv")):
        for row in rows(args.supplemental / filename):
            item = int(row["ItemId"])
            if item:
                sources[item].add(f"{kind} progression, stage {row['Stage']}")
                quest = int(row["QuestId"])
                if quest:
                    relic_quests[item] = quest
    for row in rows(args.supplemental / "StoreItem.csv"):
        sources[int(row["ItemId"])].add("FFXIV Online Store")

    supplement_types = {1: "Desynthesis", 2: "Aetherial reduction", 3: "Loot",
                        7: "Item coffer", 8: "Palace of the Dead", 9: "Heaven-on-High",
                        10: "Eureka Orthos", 11: "Eureka Anemos", 12: "Eureka Pagos",
                        13: "Eureka Pyros", 14: "Eureka Hydatos", 15: "Bozja",
                        17: "Pilgrim's Traverse"}
    for row in rows(args.supplemental / "ItemSupplement.csv"):
        kind = supplement_types.get(int(row["ItemSupplementSource"]))
        source_name = item_names.get(int(row["SourceItemId"]))
        if kind and source_name:
            sources[int(row["ItemId"])].add(f"{kind}: {source_name}")

    unobtainable = sorted({int(row["ItemId"])
                           for row in rows(args.supplemental / "UnobtainableItem.csv")
                           if row["ItemId"].isdigit()})
    result = {"source": "LuminaSupplemental (GPL-3.0), local personal-use import",
              "sources": {str(item): sorted(labels) for item, labels in sorted(sources.items())},
              "relicQuests": {str(item): quest for item, quest in sorted(relic_quests.items())},
              "unobtainable": unobtainable}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"Indexed sources for {len(result['sources'])} items and {len(unobtainable)} unavailable item IDs -> {args.out}")


if __name__ == "__main__":
    main()
