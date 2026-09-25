#!/usr/bin/env python3
# EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
# Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.
"""
Builds the EorzeaGuide library: one SQLite database holding every quest (with per-step
locations), NPC placement, zone, aetheryte, vista, aether current, achievement, hunt target,
elite mark spawn point, duty, and piece of equipment with where it comes from.

Inputs (all fetched/derived locally, none committed to the repo):
  --csv    English sheet CSVs from github.com/xivapi/ffxiv-datamining (csv/en), current patch
  --quests Questionable repo tarball (AGPL-3.0; used as data, not redistributed)
  --spawns HuntHelper SpawnPointData.json (MIT, bundled in the plugin)

  python3 tools/build_library.py --csv ~/Downloads/eg-data/csv \
      --quests ~/Downloads/eg-research/questionable.tgz \
      --spawns EorzeaGuide/Data/SpawnPointData.json --out ~/Downloads/eg-data/eorzeaguide-library.sqlite
"""
import argparse, csv, io, json, os, re, sqlite3, sys, tarfile, time, urllib.request

csv.field_size_limit(sys.maxsize)
CSV_BASE = "https://raw.githubusercontent.com/xivapi/ffxiv-datamining/master/csv/en/"
SHEETS = """Quest Level ENpcResident ENpcBase EObjName EObj TerritoryType PlaceName Map Aetheryte Adventure
AetherCurrent AetherCurrentCompFlgSet Achievement AchievementCategory AchievementKind Title MobHuntOrder
MobHuntOrderType MobHuntTarget BNpcName NotoriousMonster NotoriousMonsterTerritory ContentFinderCondition
ContentType Item ItemUICategory EquipSlotCategory ClassJobCategory ClassJob GilShopItem GilShop SpecialShop
Recipe RecipeLevelTable CraftType MapMarker ExVersion JournalGenre JournalCategory JournalSection EventItem Festival
BeastTribe GrandCompany Emote InstanceContent Fate""".split()


def fetch(csv_dir):
    os.makedirs(csv_dir, exist_ok=True)
    for s in SHEETS:
        p = os.path.join(csv_dir, s + ".csv")
        if not os.path.exists(p) or os.path.getsize(p) == 0:
            urllib.request.urlretrieve(CSV_BASE + s + ".csv", p)


def load(csv_dir, name):
    """Returns (header, rows) where rows are dicts keyed by column name; '#' is the row id."""
    with open(os.path.join(csv_dir, name + ".csv"), newline="", encoding="utf-8") as f:
        r = csv.reader(f)
        header = next(r)
        rows = [dict(zip(header, row)) for row in r]
    return header, rows


def i(v, default=0):
    try:
        return int(float(v))
    except (TypeError, ValueError):
        return default


def fl(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return 0.0


def to_map(v, size_factor, offset):
    """World -> the 1..42 map coordinate the game shows (same formula as Dalamud MapUtil)."""
    if not size_factor:
        return None
    return round(0.02 * offset + 2048.0 / size_factor + 0.02 * v + 1.0, 1)


def to_world(coord, size_factor, offset):
    return (coord - 1.0 - 2048.0 / size_factor) / 0.02 - offset


def cap(s):
    return s[:1].upper() + s[1:] if s else s


SCHEMA = """
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE expansions(id INTEGER PRIMARY KEY, name TEXT);
CREATE TABLE maps(id INTEGER PRIMARY KEY, territory INTEGER, place TEXT, size_factor INTEGER, offset_x INTEGER, offset_y INTEGER);
CREATE TABLE zones(territory INTEGER PRIMARY KEY, name TEXT, region TEXT, expansion INTEGER, map INTEGER,
                   intended_use INTEGER, aether_current_set INTEGER, is_duty INTEGER);
CREATE TABLE npc_locations(object_id INTEGER PRIMARY KEY, kind TEXT, name TEXT, territory INTEGER, map INTEGER,
                           x REAL, y REAL, z REAL, map_x REAL, map_y REAL);
CREATE TABLE quests(id INTEGER PRIMARY KEY, short_id INTEGER, name TEXT, level INTEGER, expansion INTEGER,
                    genre TEXT, category TEXT, section TEXT, kind TEXT, repeatable INTEGER, festival INTEGER,
                    grand_company INTEGER, beast_tribe INTEGER, class_job_category INTEGER, class_jobs TEXT,
                    issuer_id INTEGER, issuer TEXT, issuer_territory INTEGER, issuer_x REAL, issuer_y REAL, issuer_z REAL,
                    issuer_map_x REAL, issuer_map_y REAL, turnin_id INTEGER, turnin TEXT,
                    prereq_join INTEGER, unlocks_instance_content INTEGER, sort_key INTEGER, has_detailed_path INTEGER,
                    gil_reward INTEGER, exp_factor INTEGER);
CREATE TABLE quest_prereqs(quest INTEGER, prereq INTEGER);
CREATE TABLE quest_locks(quest INTEGER, lock_quest INTEGER);
CREATE TABLE quest_rewards(quest INTEGER, item INTEGER, count INTEGER, optional INTEGER);
CREATE TABLE quest_steps(quest INTEGER, seq INTEGER, idx INTEGER, source TEXT, action TEXT, territory INTEGER,
                         x REAL, y REAL, z REAL, map_x REAL, map_y REAL, data_id INTEGER, target TEXT,
                         aether_current INTEGER, fly INTEGER, comment TEXT);
CREATE TABLE aetherytes(id INTEGER PRIMARY KEY, name TEXT, territory INTEGER, x REAL, y REAL, z REAL,
                        map_x REAL, map_y REAL, required_quest INTEGER, is_aethernet_shard INTEGER);
CREATE TABLE vistas(adventure_id INTEGER PRIMARY KEY, idx INTEGER, name TEXT, description TEXT, territory INTEGER,
                    x REAL, y REAL, z REAL, map_x REAL, map_y REAL, emote TEXT, min_time INTEGER, max_time INTEGER,
                    min_level INTEGER);
CREATE TABLE aether_currents(id INTEGER PRIMARY KEY, territory INTEGER, quest INTEGER, eobj INTEGER,
                             x REAL, y REAL, z REAL, map_x REAL, map_y REAL);
CREATE TABLE achievements(id INTEGER PRIMARY KEY, name TEXT, description TEXT, kind TEXT, category TEXT,
                          points INTEGER, type INTEGER, key INTEGER, data TEXT, reward_item INTEGER, title TEXT);
CREATE TABLE achievement_quests(achievement INTEGER, quest INTEGER);
CREATE TABLE hunt_bills(mark_index INTEGER PRIMARY KEY, name TEXT, type INTEGER, order_start INTEGER, order_amount INTEGER);
CREATE TABLE hunt_targets(mark_index INTEGER, order_row INTEGER, mob_index INTEGER, bnpc_name_id INTEGER, name TEXT,
                          needed_kills INTEGER, rank INTEGER, territory INTEGER, zone TEXT, fate INTEGER);
CREATE TABLE elite_marks(territory INTEGER, bnpc_name_id INTEGER, name TEXT, rank TEXT);
CREATE TABLE hunt_spawn_points(territory INTEGER, map_x REAL, map_y REAL, x REAL, z REAL);
CREATE TABLE duties(cfc_id INTEGER PRIMARY KEY, name TEXT, content_type TEXT, level INTEGER, item_level INTEGER,
                    expansion INTEGER, territory INTEGER, instance_content INTEGER, unlock_quest INTEGER, high_end INTEGER);
CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT, item_level INTEGER, equip_level INTEGER, slot TEXT,
                   ui_category TEXT, jobs TEXT, rarity INTEGER, untradable INTEGER, can_hq INTEGER, is_gear INTEGER);
CREATE TABLE item_sources(item INTEGER, source TEXT, detail TEXT, npc_id INTEGER, npc TEXT, territory INTEGER,
                          map_x REAL, map_y REAL, cost_item TEXT, cost INTEGER);
"""

INDEXES = """
CREATE INDEX ix_steps_quest ON quest_steps(quest, seq, idx);
CREATE INDEX ix_steps_terr ON quest_steps(territory);
CREATE INDEX ix_quests_terr ON quests(issuer_territory);
CREATE INDEX ix_quests_name ON quests(name);
CREATE INDEX ix_prereq ON quest_prereqs(quest);
CREATE INDEX ix_prereq_rev ON quest_prereqs(prereq);
CREATE INDEX ix_src_item ON item_sources(item);
CREATE INDEX ix_items_slot ON items(slot, item_level);
CREATE INDEX ix_npc_terr ON npc_locations(territory);
CREATE VIEW v_zone_checklist AS
  SELECT z.territory, z.name, z.region, e.name AS expansion,
    (SELECT count(*) FROM quests q WHERE q.issuer_territory = z.territory) AS quests,
    (SELECT count(*) FROM aetherytes a WHERE a.territory = z.territory AND a.is_aethernet_shard = 0) AS aetherytes,
    (SELECT count(*) FROM vistas v WHERE v.territory = z.territory) AS vistas,
    (SELECT count(*) FROM aether_currents c WHERE c.territory = z.territory) AS aether_currents,
    (SELECT count(*) FROM elite_marks m WHERE m.territory = z.territory) AS elite_marks,
    (SELECT count(*) FROM hunt_spawn_points s WHERE s.territory = z.territory) AS spawn_points
  FROM zones z LEFT JOIN expansions e ON e.id = z.expansion WHERE z.is_duty = 0;
CREATE VIEW v_quest_steps AS
  SELECT q.name AS quest, q.kind, s.seq, s.idx, s.action, s.target, z.name AS zone, s.map_x, s.map_y, s.comment, s.source
  FROM quest_steps s JOIN quests q ON q.id = s.quest LEFT JOIN zones z ON z.territory = s.territory;
CREATE VIEW v_msq AS
  SELECT q.id, q.name, q.level, e.name AS expansion, q.issuer, z.name AS zone, q.issuer_map_x, q.issuer_map_y
  FROM quests q LEFT JOIN expansions e ON e.id = q.expansion LEFT JOIN zones z ON z.territory = q.issuer_territory
  WHERE q.kind = 'MainScenario' ORDER BY q.expansion, q.sort_key, q.id;
CREATE VIEW v_gear AS
  SELECT i.id, i.name, i.item_level, i.equip_level, i.slot, i.jobs,
         group_concat(s.source || coalesce(': ' || s.detail, ''), ' | ') AS how_to_get
  FROM items i LEFT JOIN item_sources s ON s.item = i.id WHERE i.is_gear = 1 GROUP BY i.id;
"""


def build(args):
    t0 = time.time()
    C = args.csv
    if args.fetch:
        fetch(C)
    if os.path.exists(args.out):
        os.remove(args.out)
    db = sqlite3.connect(args.out)
    db.executescript(SCHEMA)
    ins = lambda table, rows: db.executemany(
        f"INSERT INTO {table} VALUES ({','.join('?' * len(rows[0]))})", rows) if rows else None

    sheet = {}
    for s in SHEETS:
        sheet[s] = load(C, s)[1]
    byid = {s: {r["#"]: r for r in rows} for s, rows in sheet.items()}

    def name_of(s, rid, col="Name"):
        r = byid[s].get(str(rid))
        return r.get(col, "") if r else ""

    # ---- expansions, maps, zones
    ins("expansions", [(i(r["#"]), r["Name"]) for r in sheet["ExVersion"]])
    maps = {}
    for r in sheet["Map"]:
        m = (i(r["#"]), i(r["TerritoryType"]), name_of("PlaceName", r["PlaceName"]), i(r["SizeFactor"]), i(r["OffsetX"]), i(r["OffsetY"]))
        maps[m[0]] = m
    ins("maps", list(maps.values()))

    def mc(map_id, x, z):
        m = maps.get(i(map_id))
        if not m:
            return None, None
        return to_map(x, m[3], m[4]), to_map(z, m[3], m[5])

    terr_map = {}
    zones = []
    for r in sheet["TerritoryType"]:
        name = name_of("PlaceName", r["PlaceName"])
        if not name or r["IsPvpZone"] == "True":
            continue
        tid = i(r["#"])
        terr_map[tid] = i(r["Map"])
        zones.append((tid, name, name_of("PlaceName", r["PlaceNameRegion"]), i(r["ExVersion"]), i(r["Map"]),
                      i(r["TerritoryIntendedUse"]), i(r["AetherCurrentCompFlgSet"]), 1 if i(r["ContentFinderCondition"]) else 0))
    ins("zones", zones)
    zone_name = {z[0]: z[1] for z in zones}

    # ---- NPC / object placements (first Level row per object)
    names = {}
    for r in sheet["ENpcResident"]:
        if r["Singular"]:
            names[i(r["#"])] = cap(r["Singular"])
    for r in sheet["EObjName"]:
        if r["Singular"]:
            names[i(r["#"])] = cap(r["Singular"])
    npc = {}
    levels = byid["Level"]
    for r in sheet["Level"]:
        obj = i(r["Object"])
        if obj == 0 or obj in npc or i(r["Territory"]) == 0:
            continue
        x, y, z = fl(r["X"]), fl(r["Y"]), fl(r["Z"])
        mx, my = mc(r["Map"], x, z)
        kind = "enpc" if 1000000 <= obj < 2000000 else "eobj" if 2000000 <= obj < 3000000 else "other"
        npc[obj] = (obj, kind, names.get(obj, ""), i(r["Territory"]), i(r["Map"]), x, y, z, mx, my)
    ins("npc_locations", list(npc.values()))

    def level_point(level_id):
        l = levels.get(str(level_id))
        if not l or i(l["Territory"]) == 0:
            return None
        x, y, z = fl(l["X"]), fl(l["Y"]), fl(l["Z"])
        mx, my = mc(l["Map"], x, z)
        return i(l["Territory"]), x, y, z, mx, my

    # ---- quests
    genre = byid["JournalGenre"]; cat = byid["JournalCategory"]; sect = byid["JournalSection"]
    cjc = byid["ClassJobCategory"]
    quest_rows, prereqs, locks, rewards, steps = [], [], [], [], []
    qnames = {}
    for r in sheet["Quest"]:
        qid = i(r["#"])
        if qid < 65536 or not r["Name"]:
            continue
        qnames[qid] = r["Name"]
        g = genre.get(r["JournalGenre"], {})
        c = cat.get(g.get("JournalCategory", ""), {})
        s = sect.get(c.get("JournalSection", ""), {})
        section, category = s.get("Name", ""), c.get("Name", "")
        festival, tribe, rep = i(r["Festival"]), i(r["BeastTribe"]), r.get("IsRepeatable") == "True"
        sl, cl = section.lower(), category.lower()
        kind = ("Seasonal" if festival else "MainScenario" if "main scenario" in sl else
                "Feature" if ("chronicles" in sl or "feature" in sl) else
                "AlliedSociety" if (tribe or "allied" in cl or "beast" in cl or "tribal" in cl) else
                "ClassJob" if ("class" in sl or "job" in sl or "role quest" in cl) else
                "Repeatable" if rep else "Side" if section else "Other")
        ip = level_point(r["IssuerLocation"])
        issuer_id, turnin_id = i(r["IssuerStart"]), i(r["TargetEnd"])
        quest_rows.append((qid, qid - 65536, r["Name"], i(r["ClassJobLevel[0]"]), i(r["Expansion"]), g.get("Name", ""),
                           category, section, kind, int(rep), festival, i(r["GrandCompany"]), tribe,
                           i(r["ClassJobCategory0"]), cjc.get(r["ClassJobCategory0"], {}).get("Name", ""),
                           issuer_id, names.get(issuer_id, ""), *(ip[:4] if ip else (None,) * 4), *(ip[4:] if ip else (None, None)),
                           turnin_id, names.get(turnin_id, ""), i(r["PreviousQuestJoin"]), i(r["InstanceContentUnlock"]),
                           i(r["SortKey"]), 0, i(r["GilReward"]), i(r["ExpFactor"])))
        for k in range(3):
            p = i(r.get(f"PreviousQuest[{k}]"))
            if p:
                prereqs.append((qid, p))
        for k in range(2):
            p = i(r.get(f"QuestLock[{k}]"))
            if p:
                locks.append((qid, p))
        for k in range(7):
            it = i(r.get(f"Reward[{k}]"))
            if 0 < it < 2000000:
                rewards.append((qid, it, i(r.get(f"ItemCountReward[{k}]")), 0))
        for k in range(5):
            it = i(r.get(f"OptionalItemReward[{k}]"))
            if it:
                rewards.append((qid, it, i(r.get(f"OptionalItemCountReward[{k}]")), 1))
        # Game-data fallback steps: TodoParams objective areas + the turn-in NPC.
        idx = 0
        for t in range(24):
            seq = r.get(f"TodoParams[{t}].ToDoCompleteSeq")
            if seq is None:
                break
            for k in range(8):
                lp = level_point(r.get(f"TodoParams[{t}].ToDoLocation[{k}]"))
                if lp:
                    steps.append((qid, i(seq), idx, "gamedata", "Objective", lp[0], lp[1], lp[2], lp[3], lp[4], lp[5], 0, "", 0, 0, ""))
                    idx += 1
        if turnin_id in npc:
            n = npc[turnin_id]
            steps.append((qid, 255, idx, "gamedata", "CompleteQuest", n[3], n[5], n[6], n[7], n[8], n[9], turnin_id, n[2], 0, 0, ""))

    # Questionable hand-mapped steps replace the game-data fallback for the quests they cover.
    mapped = set()
    if args.quests and os.path.exists(args.quests):
        qsteps = []
        with tarfile.open(args.quests, "r:gz") as tf:
            for m in tf.getmembers():
                if not m.isfile() or "/QuestPaths/" not in m.name or not m.name.endswith(".json"):
                    continue
                base = os.path.basename(m.name)
                mm = re.match(r"^(\d+)_", base)
                if not mm:
                    continue
                qid = int(mm.group(1)) + 65536
                try:
                    data = json.loads(tf.extractfile(m).read().decode("utf-8-sig"))
                except Exception:
                    continue
                idx = 0
                for seqo in data.get("QuestSequence", []):
                    seq = seqo.get("Sequence", 0)
                    for st in seqo.get("Steps", []):
                        pos = st.get("Position")
                        terr = st.get("TerritoryId", 0)
                        x = y = z = None; mx = my = None
                        if pos:
                            x, y, z = pos["X"], pos["Y"], pos["Z"]
                            mx, my = mc(terr_map.get(terr, 0), x, z)
                        did = st.get("DataId", 0) or 0
                        qsteps.append((qid, seq, idx, "questionable", st.get("InteractionType", ""), terr, x, y, z, mx, my,
                                       did, names.get(did, ""), st.get("AetherCurrentId", 0) or 0, int(bool(st.get("Fly"))),
                                       st.get("Comment", "")))
                        idx += 1
                mapped.add(qid)
        steps = [s for s in steps if s[0] not in mapped] + qsteps
    quest_rows = [q[:28] + (1 if q[0] in mapped else 0,) + q[29:] for q in quest_rows]
    ins("quests", quest_rows); ins("quest_prereqs", prereqs); ins("quest_locks", locks)
    ins("quest_rewards", rewards); ins("quest_steps", steps)

    # ---- aetherytes, vistas, aether currents
    # Aetheryte.Level points at layout rows that are not in the Level sheet; the reliable source is the
    # map's own marker list (Map.MapMarkerRange -> MapMarker, DataType 3 = aetheryte, 4 = aethernet shard),
    # in 2048-pixel map-texture coordinates. Height is not stored there.
    markers = {}
    for r in sheet["MapMarker"]:
        if r["DataType"] in ("3", "4"):   # 3: keyed by Aetheryte id; 4: aethernet shard keyed by its PlaceName id
            markers.setdefault((int(r["#"].split(".")[0]), r["DataType"], i(r["DataKey"])), (i(r["X"]), i(r["Y"])))
    ae = []
    for r in sheet["Aetheryte"]:
        nm = name_of("PlaceName", r["PlaceName"]) or name_of("PlaceName", r["AethernetName"])
        terr = i(r["Territory"])
        # City shards carry Map = 0 and multi-level cities have several maps: try each map of the territory.
        cands = [i(r["Map"])] if i(r["Map"]) else []
        cands += [m for m, v in maps.items() if v[1] == terr and m not in cands]
        px = mp = None
        for map_id in cands:
            rng = i(byid["Map"].get(str(map_id), {}).get("MapMarkerRange"))
            px = markers.get((rng, "3", i(r["#"]))) or markers.get((rng, "4", i(r["AethernetName"])))
            if px:
                mp = maps[map_id]
                break
        if not nm or not px:
            continue
        c = mp[3] / 100.0
        x, z = (px[0] - 1024) / c - mp[4], (px[1] - 1024) / c - mp[5]
        ae.append((i(r["#"]), nm, i(r["Territory"]), x, None, z, to_map(x, mp[3], mp[4]), to_map(z, mp[3], mp[5]),
                   i(r["RequiredQuest"]), 0 if r["IsAetheryte"] == "True" else 1))
    ins("aetherytes", ae)
    vis = []
    for r in sheet["Adventure"]:
        lp = level_point(r["Level"])
        if not lp:
            continue
        vis.append((i(r["#"]), i(r["#"]) - 2162688, r["Name"], r["Description"], lp[0], lp[1], lp[2], lp[3], lp[4], lp[5],
                    name_of("Emote", r["Emote"]), i(r["MinTime"]), i(r["MaxTime"]), i(r["MinLevel"])))
    ins("vistas", vis)
    eobj_for = {}
    for r in sheet["EObj"]:
        d = i(r["Data"])
        if 2818048 <= d < 2818048 + 2000:
            eobj_for.setdefault(d, i(r["#"]))
    cur_rows = []
    for r in sheet["AetherCurrentCompFlgSet"]:
        terr = i(r["Territory"])
        for k in range(15):
            cid = i(r.get(f"AetherCurrents[{k}]"))
            if not cid:
                continue
            quest = i(byid["AetherCurrent"].get(str(cid), {}).get("Quest"))
            eo = eobj_for.get(cid, 0)
            n = npc.get(eo)
            cur_rows.append((cid, terr, quest, eo, *(n[5:10] if n else (None,) * 5)))
    ins("aether_currents", cur_rows)

    # ---- achievements
    ach, ach_q = [], []
    for r in sheet["Achievement"]:
        if not r["Name"]:
            continue
        data = [i(r.get(f"Data[{k}]")) for k in range(8)]
        c = byid["AchievementCategory"].get(r["AchievementCategory"], {})
        ach.append((i(r["#"]), r["Name"], r["Description"], name_of("AchievementKind", c.get("AchievementKind", "")),
                    c.get("Name", ""), i(r["Points"]), i(r["Type"]), i(r["Key"]), json.dumps(data), i(r["Item"]),
                    name_of("Title", r["Title"], "Masculine")))
        for v in [i(r["Key"])] + data:
            if v in qnames:
                ach_q.append((i(r["#"]), v))
    ins("achievements", ach); ins("achievement_quests", ach_q)

    # ---- hunts
    bills, targets = [], []
    orders = {}
    for r in sheet["MobHuntOrder"]:
        row, sub = r["#"].split(".")
        orders.setdefault(int(row), []).append((int(sub), r))
    for r in sheet["MobHuntOrderType"]:
        mi, start, amount = i(r["#"]), i(r["OrderStart"]), i(r["OrderAmount"])
        if not amount:
            continue
        bname = cap(name_of("EventItem", r["EventItem"], "Name")) or f"Hunt bill {mi}"
        bills.append((mi, bname, i(r["Type"]), start, amount))
        for row in range(start, start + amount):
            for sub, o in sorted(orders.get(row, [])):
                t = byid["MobHuntTarget"].get(o["Target"])
                if not t:
                    continue
                mp = maps.get(i(t["Map"]))
                targets.append((mi, row, sub, i(t["Name"]), cap(name_of("BNpcName", t["Name"], "Singular")), i(o["NeededKills"]),
                                i(o["Rank"]), mp[1] if mp else 0, name_of("PlaceName", t["PlaceName"]), i(t["FATE"])))
    ins("hunt_bills", bills); ins("hunt_targets", targets)
    elite = []
    rank = {1: "B", 2: "A", 3: "S"}
    for r in sheet["TerritoryType"]:
        nmt = byid["NotoriousMonsterTerritory"].get(r["NotoriousMonsterTerritory"])
        if not nmt:
            continue
        for k in range(10):
            m = byid["NotoriousMonster"].get(nmt.get(f"NotoriousMonsters[{k}]", ""))
            if m and i(m["BNpcName"]):
                elite.append((i(r["#"]), i(m["BNpcName"]), cap(name_of("BNpcName", m["BNpcName"], "Singular")), rank.get(i(m["Rank"]), m["Rank"])))
    ins("elite_marks", elite)
    if args.spawns and os.path.exists(args.spawns):
        sp = []
        for m in json.load(open(args.spawns)):
            terr = m["MapID"]
            mp = maps.get(terr_map.get(terr, 0))
            for p in m["Positions"]:
                wx = to_world(p["X"], mp[3], mp[4]) if mp else None
                wz = to_world(p["Y"], mp[3], mp[5]) if mp else None
                sp.append((terr, p["X"], p["Y"], wx, wz))
        ins("hunt_spawn_points", sp)

    # ---- duties
    unlock_by = {}
    for q in quest_rows:
        if q[26]:
            unlock_by.setdefault(q[26], q[0])
    duties = []
    for r in sheet["ContentFinderCondition"]:
        if not r["Name"] or r["PvP"] == "True":
            continue
        ic = i(r["Content"]) if i(r["ContentLinkType"]) == 1 else 0
        duties.append((i(r["#"]), cap(r["Name"]), name_of("ContentType", r["ContentType"]), i(r["ClassJobLevelRequired"]),
                       i(r["ItemLevelRequired"]), i(r["RequiredExVersion"]), i(r["TerritoryType"]), ic,
                       unlock_by.get(ic, 0) if ic else 0, 1 if r["HighEndDuty"] == "True" else 0))
    ins("duties", duties)

    # ---- items + sources
    slot_cols = ["MainHand", "OffHand", "Head", "Body", "Gloves", "Waist", "Legs", "Feet", "Ears", "Neck", "Wrists", "FingerL", "FingerR", "SoulCrystal"]
    slot_name = {"MainHand": "Main hand", "OffHand": "Off hand", "Gloves": "Hands", "FingerL": "Ring", "FingerR": "Ring", "SoulCrystal": "Soul crystal"}
    esc = byid["EquipSlotCategory"]
    items = []
    for r in sheet["Item"]:
        if not r["Name"]:
            continue
        e = esc.get(r["EquipSlotCategory"])
        slot = ""
        if e and r["EquipSlotCategory"] != "0":
            slot = next((slot_name.get(c, c) for c in slot_cols if i(e.get(c)) > 0), "Other")
        items.append((i(r["#"]), r["Name"], i(r["LevelItem"]), i(r["LevelEquip"]), slot, name_of("ItemUICategory", r["ItemUICategory"]),
                      cjc.get(r["ClassJobCategory"], {}).get("Name", ""), i(r["Rarity"]), int(r["IsUntradable"] == "True"),
                      int(r["CanBeHq"] == "True"), 1 if slot else 0))
    ins("items", items)
    item_name = {it[0]: it[1] for it in items}

    shop_npcs = {}
    for r in sheet["ENpcBase"]:
        for k in range(32):
            d = i(r.get(f"ENpcData[{k}]"))
            if d:
                shop_npcs.setdefault(d, [])
                if len(shop_npcs[d]) < 3:
                    shop_npcs[d].append(i(r["#"]))

    def npc_at(shop):
        for n in shop_npcs.get(shop, []):
            if n in npc:
                p = npc[n]
                return n, p[2], p[3], p[8], p[9]
        return 0, "", 0, None, None

    src = []
    for r in sheet["GilShopItem"]:
        shop = int(r["#"].split(".")[0])
        it = i(r["Item"])
        if it:
            n = npc_at(shop)
            src.append((it, "gil shop", name_of("GilShop", shop), n[0], n[1], n[2], n[3], n[4], "Gil", i(byid["Item"].get(str(it), {}).get("PriceMid"))))
    for r in sheet["SpecialShop"]:
        shop = i(r["#"])
        n = npc_at(shop)
        for e in range(60):
            for k in range(2):
                it = i(r.get(f"Item[{e}].Item[{k}]"))
                if not it:
                    continue
                cost_it = i(r.get(f"Item[{e}].ItemCost[0]"))
                src.append((it, "exchange", r["Name"], n[0], n[1], n[2], n[3], n[4], item_name.get(cost_it, str(cost_it) if cost_it else ""),
                            i(r.get(f"Item[{e}].CurrencyCost[0]"))))
    rlt = byid["RecipeLevelTable"]
    for r in sheet["Recipe"]:
        it = i(r["ItemResult"])
        if it:
            src.append((it, "crafted", f'{name_of("CraftType", r["CraftType"])} lv {i(rlt.get(r["RecipeLevelTable"], {}).get("ClassJobLevel"))}',
                        0, "", 0, None, None, "", 0))
    for q in rewards:
        src.append((q[1], "quest reward", qnames.get(q[0], ""), 0, "", 0, None, None, "", q[0]))
    for a in ach:
        if a[9]:
            src.append((a[9], "achievement reward", a[1], 0, "", 0, None, None, "", a[0]))
    ins("item_sources", src)

    db.executescript(INDEXES)
    counts = {t: db.execute(f"SELECT count(*) FROM {t}").fetchone()[0] for t in
              ["quests", "quest_steps", "npc_locations", "zones", "aetherytes", "vistas", "aether_currents", "achievements",
               "hunt_targets", "elite_marks", "hunt_spawn_points", "duties", "items", "item_sources"]}
    ins("meta", [("built", time.strftime("%Y-%m-%d %H:%M")), ("csv_source", "github.com/xivapi/ffxiv-datamining csv/en"),
                 ("questionable_quests", str(len(mapped))), ("counts", json.dumps(counts))])
    db.commit()
    db.execute("VACUUM")
    db.close()
    print(json.dumps(counts, indent=1))
    print(f"questionable-mapped quests: {len(mapped)}; built in {time.time() - t0:.1f}s -> {args.out} ({os.path.getsize(args.out) / 1e6:.1f} MB)")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", required=True)
    ap.add_argument("--quests")
    ap.add_argument("--spawns")
    ap.add_argument("--out", required=True)
    ap.add_argument("--fetch", action="store_true", help="download missing CSVs first")
    build(ap.parse_args())
