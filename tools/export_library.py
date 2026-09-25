#!/usr/bin/env python3
# EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
# Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.
"""
Turns the library database into readable documents:
  <out>/zones/<NN Expansion>/<Zone>.md   checklist per zone: aetherytes, vistas, aether currents,
                                          quests (by level, with giver + map coords), elite marks, hunt targets
  <out>/msq/<NN Expansion>.md             the main scenario in order, with where each quest starts
  <out>/csv/<table>.csv                   every curated table, for spreadsheets
  <out>/README.md                         index + counts

  python3 tools/export_library.py ~/Downloads/eg-data/eorzeaguide-library.sqlite <out dir>
"""
import csv, json, os, re, sqlite3, sys

db = sqlite3.connect(sys.argv[1])
out = sys.argv[2]
db.row_factory = sqlite3.Row


def safe(s):
    return re.sub(r'[\\/:*?"<>|]', "-", s).strip() or "unnamed"


def coord(x, y):
    return f"({x:.1f}, {y:.1f})" if x is not None and y is not None else "-"


exp = {r["id"]: r["name"] for r in db.execute("SELECT * FROM expansions")}
zname = {r["territory"]: r["name"] for r in db.execute("SELECT territory, name FROM zones")}
os.makedirs(out, exist_ok=True)

# ---- per-zone checklists (only zones that have something to do)
zones = db.execute("SELECT * FROM v_zone_checklist WHERE quests + aetherytes + vistas + aether_currents + elite_marks > 0 ORDER BY expansion, region, name").fetchall()
written = 0
for z in zones:
    t = z["territory"]
    zrow = db.execute("SELECT expansion FROM zones WHERE territory = ?", (t,)).fetchone()
    edir = os.path.join(out, "zones", f"{zrow['expansion']:02d} {exp.get(zrow['expansion'], '?')}")
    os.makedirs(edir, exist_ok=True)
    L = [f"# {z['name']}", "", f"*{z['region']} - {exp.get(zrow['expansion'], '?')}*", "",
         "| Aetherytes | Vistas | Aether currents | Quests starting here | Elite marks | S/A/B spawn points |",
         "|---|---|---|---|---|---|",
         f"| {z['aetherytes']} | {z['vistas']} | {z['aether_currents']} | {z['quests']} | {z['elite_marks']} | {z['spawn_points']} |", ""]
    ae = db.execute("SELECT name, map_x, map_y FROM aetherytes WHERE territory = ? AND is_aethernet_shard = 0", (t,)).fetchall()
    if ae:
        L += ["## Aetherytes", ""] + [f"- [ ] {a['name']} {coord(a['map_x'], a['map_y'])}" for a in ae] + [""]
    vi = db.execute("SELECT idx, name, map_x, map_y, emote, min_time, max_time FROM vistas WHERE territory = ? ORDER BY idx", (t,)).fetchall()
    if vi:
        L += ["## Sightseeing log", ""]
        for v in vi:
            tm = f", {v['min_time']:04d}-{v['max_time']:04d} ET" if v["max_time"] else ""
            L.append(f"- [ ] #{v['idx']} {v['name']} {coord(v['map_x'], v['map_y'])} - emote: {v['emote'] or '?'}{tm}")
        L.append("")
    ac = db.execute("""SELECT c.id, c.map_x, c.map_y, q.name AS quest FROM aether_currents c LEFT JOIN quests q ON q.id = c.quest
                       WHERE c.territory = ? ORDER BY c.quest != 0, c.id""", (t,)).fetchall()
    if ac:
        L += ["## Aether currents", ""]
        L += [f"- [ ] {'Quest: ' + a['quest'] if a['quest'] else 'Field current ' + coord(a['map_x'], a['map_y'])}" for a in ac] + [""]
    qs = db.execute("""SELECT id, name, level, kind, issuer, issuer_map_x, issuer_map_y, has_detailed_path FROM quests
                       WHERE issuer_territory = ? ORDER BY kind = 'MainScenario' DESC, level, name""", (t,)).fetchall()
    if qs:
        L += ["## Quests", "", "| | Lv | Quest | Type | Giver | Where | Step path |", "|---|---|---|---|---|---|---|"]
        for q in qs:
            L.append(f"| [ ] | {q['level']} | {q['name']} | {q['kind']} | {q['issuer'] or '-'} | {coord(q['issuer_map_x'], q['issuer_map_y'])} | {'mapped' if q['has_detailed_path'] else 'area'} |")
        L.append("")
    em = db.execute("SELECT name, rank FROM elite_marks WHERE territory = ? ORDER BY rank DESC", (t,)).fetchall()
    if em:
        L += ["## Elite marks", ""] + [f"- **{m['rank']}** {m['name']}" for m in em] + [""]
    ht = db.execute("""SELECT DISTINCT t.name, b.name AS bill, t.needed_kills FROM hunt_targets t JOIN hunt_bills b ON b.mark_index = t.mark_index
                       WHERE t.territory = ? ORDER BY b.mark_index, t.name""", (t,)).fetchall()
    if ht:
        L += ["## Hunt bill targets", ""] + [f"- {h['name']} x{h['needed_kills']} ({h['bill']})" for h in ht] + [""]
    with open(os.path.join(edir, safe(z["name"]) + ".md"), "w") as f:
        f.write("\n".join(L))
    written += 1

# ---- MSQ in order
os.makedirs(os.path.join(out, "msq"), exist_ok=True)
for e, name in exp.items():
    rows = db.execute("SELECT * FROM v_msq WHERE expansion = ?", (name,)).fetchall()
    if not rows:
        continue
    L = [f"# Main scenario - {name}", "", f"{len(rows)} quests, in guide order.", "", "| # | Lv | Quest | Starts with | Zone | Where |", "|---|---|---|---|---|---|"]
    for n, q in enumerate(rows, 1):
        L.append(f"| {n} | {q['level']} | {q['name']} | {q['issuer'] or '-'} | {q['zone'] or '-'} | {coord(q['issuer_map_x'], q['issuer_map_y'])} |")
    with open(os.path.join(out, "msq", f"{e:02d} {safe(name)}.md"), "w") as f:
        f.write("\n".join(L))

# ---- CSV exports
os.makedirs(os.path.join(out, "csv"), exist_ok=True)
for table in ["quests", "quest_steps", "zones", "npc_locations", "aetherytes", "vistas", "aether_currents", "achievements",
              "hunt_targets", "elite_marks", "hunt_spawn_points", "duties", "items", "item_sources"]:
    cur = db.execute(f"SELECT * FROM {table}")
    with open(os.path.join(out, "csv", table + ".csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow([d[0] for d in cur.description])
        w.writerows(cur)

meta = dict(db.execute("SELECT key, value FROM meta").fetchall())
counts = json.loads(meta["counts"])
readme = ["# EorzeaGuide library", "", f"Built {meta['built']} from {meta['csv_source']} (current patch) + Questionable quest paths "
          f"({meta['questionable_quests']} quests hand-mapped) + HuntHelper spawn points.", "",
          "`eorzeaguide-library.sqlite` is the source; everything here is generated from it by `tools/export_library.py`.", "",
          "| Table | Rows |", "|---|---|"] + [f"| {k} | {v:,} |" for k, v in counts.items()] + [
          "", f"- `zones/` - {written} zone checklists, by expansion", "- `msq/` - the main scenario in order, per expansion",
          "- `csv/` - every table for spreadsheets", "",
          "Personal use. Game data belongs to Square Enix; Questionable data is AGPL-3.0; HuntHelper data is MIT. Not for redistribution."]
with open(os.path.join(out, "README.md"), "w") as f:
    f.write("\n".join(readme))
print(f"{written} zone docs, MSQ docs, {len(counts)} CSVs -> {out}")
