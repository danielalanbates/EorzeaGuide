#!/usr/bin/env python3
# EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
# Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.
"""
Checks the library against independent data and prints PASS/FAIL per check.

  python3 tools/verify_library.py ~/Downloads/eg-data/eorzeaguide-library.sqlite <Questionable repo dir>
"""
import math, re, sqlite3, sys

db = sqlite3.connect(sys.argv[1])
qdir = sys.argv[2]
fails = 0


def check(name, ok, detail):
    global fails
    print(f"{'PASS' if ok else 'FAIL'}  {name}: {detail}")
    fails += 0 if ok else 1


# 1. Aetheryte positions vs Questionable's hand-measured table.
enum = dict((n, int(v)) for n, v in re.findall(r"(\w+)\s*=\s*(\d+)", open(f"{qdir}/Questionable.Model/Common/EAetheryteLocation.cs").read()))
ref = {}
for n, a, b, c in re.findall(r"EAetheryteLocation\.(\w+),\s*new\(([-\d.]+)f?,\s*([-\d.]+)f?,\s*([-\d.]+)f?\)", open(f"{qdir}/Questionable/Data/AetheryteData.cs").read()):
    if n in enum:
        ref[enum[n]] = (float(a), float(b), float(c))
ours = {r[0]: (r[1], r[2], r[3]) for r in db.execute("SELECT id, x, z, is_aethernet_shard FROM aetherytes")}
near = [k for k, (x, z, _) in ours.items() if k in ref and math.hypot(x - ref[k][0], z - ref[k][2]) < 15]
far = [(k, ours[k], ref[k]) for k in ours if k in ref and k not in near]
invisible = set()
if len(sys.argv) > 3:   # optional: Aetheryte.csv, to exclude invisible aethernet exits (not attunable, no map marker)
    import csv
    csv.field_size_limit(sys.maxsize)
    rows_ae = list(csv.DictReader(open(sys.argv[3])))
    invisible = {int(r["#"]) for r in rows_ae if r["Invisible"] == "True"}
    sheet_ids = {int(r["#"]) for r in rows_ae}
    # Questionable also lists special shards (Firmament, island) under ids outside the Aetheryte sheet.
    ref = {k: v for k, v in ref.items() if k in sheet_ids}
missing = sorted(k for k in ref if k not in ours and k not in invisible)
check("aetheryte positions accurate", len(far) == 0, f"{len(near)} within 15y of reference, {len(far)} off" + (f" e.g. {far[:3]}" if far else ""))
check("aetheryte coverage", len(missing) == 0, f"{len(ours)} present; {len(missing)} visible reference aetherytes missing {missing[:15]}; {len([k for k in ref if k in invisible])} invisible exits excluded")

# 2. Game-data quest-giver position vs Questionable's AcceptQuest step for the same quest.
rows = db.execute("""SELECT q.id, q.issuer_territory, q.issuer_x, q.issuer_z, s.territory, s.x, s.z
                     FROM quests q JOIN quest_steps s ON s.quest = q.id AND s.action = 'AcceptQuest' AND s.x IS NOT NULL
                     WHERE q.issuer_x IS NOT NULL GROUP BY q.id""").fetchall()
good = sum(1 for r in rows if r[1] == r[4] and math.hypot(r[2] - r[5], r[3] - r[6]) < 10)
check("quest-giver positions agree", good >= 0.95 * len(rows), f"{good}/{len(rows)} within 10y of Questionable's accept step")

# 3. Coverage.
tot = db.execute("SELECT count(*) FROM quests").fetchone()[0]
with_loc = db.execute("SELECT count(*) FROM quests WHERE issuer_x IS NOT NULL OR id IN (SELECT quest FROM quest_steps WHERE x IS NOT NULL)").fetchone()[0]
detailed = db.execute("SELECT count(*) FROM quests WHERE has_detailed_path = 1").fetchone()[0]
check("quests with at least one location", with_loc >= 0.9 * tot, f"{with_loc}/{tot}; hand-mapped step paths for {detailed}")
by_kind = db.execute("SELECT kind, count(*) FROM quests GROUP BY kind ORDER BY 2 DESC").fetchall()
msq = dict(by_kind).get("MainScenario", 0)
check("main scenario looks complete", msq > 800, f"{msq} MSQ quests; by kind {by_kind}")
per_exp = db.execute("SELECT e.name, count(*) FROM quests q JOIN expansions e ON e.id = q.expansion WHERE q.kind='MainScenario' GROUP BY q.expansion").fetchall()
print(f"      MSQ per expansion: {per_exp}")

# 4. Cross-table sanity.
cur = db.execute("SELECT count(*), sum(x IS NOT NULL OR quest != 0) FROM aether_currents").fetchone()
check("aether currents locatable", cur[1] == cur[0], f"{cur[1]}/{cur[0]} have a position or a quest")
vis = db.execute("SELECT count(*), count(DISTINCT territory) FROM vistas").fetchone()
check("sightseeing log", vis[0] >= 300, f"{vis[0]} vistas across {vis[1]} zones")
gear = db.execute("SELECT count(*), sum(id IN (SELECT item FROM item_sources)) FROM items WHERE is_gear = 1").fetchone()
check("gear with a known source", gear[1] >= 0.5 * gear[0], f"{gear[1]}/{gear[0]} pieces of equipment have at least one source")
ach = db.execute("SELECT count(*), count(DISTINCT achievement) FROM achievements, (SELECT achievement FROM achievement_quests) LIMIT 1").fetchone()
aq = db.execute("SELECT count(DISTINCT achievement) FROM achievement_quests").fetchone()[0]
print(f"INFO  achievements linked to at least one quest: {aq}")
types = db.execute("SELECT type, count(*) FROM achievements GROUP BY type ORDER BY 2 DESC").fetchall()
print(f"INFO  achievement types: {types}")

print(f"\n{fails} check(s) failed")
sys.exit(1 if fails else 0)
