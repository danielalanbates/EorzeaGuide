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
    parser.add_argument("--out", type=Path, required=True,
                        help="Local Data/SupplementalGearSources.json (gitignored)")
    args = parser.parse_args()

    names = {int(row["#"]): row["Name"].strip()
             for row in rows(args.duties) if row["#"].isdigit() and row["Name"].strip()}
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

    unobtainable = sorted({int(row["ItemId"])
                           for row in rows(args.supplemental / "UnobtainableItem.csv")
                           if row["ItemId"].isdigit()})
    result = {"source": "LuminaSupplemental (GPL-3.0), local personal-use import",
              "sources": {str(item): sorted(labels) for item, labels in sorted(sources.items())},
              "unobtainable": unobtainable}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"Indexed {len(result['sources'])} items from duty loot tables and {len(unobtainable)} unavailable item IDs -> {args.out}")


if __name__ == "__main__":
    main()
