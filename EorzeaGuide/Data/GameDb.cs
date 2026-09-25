// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EorzeaGuide.Data;

/// Everything the guide knows, generated from the game's own Excel sheets at load time,
/// then enriched with Questionable step paths and HuntHelper spawn points.
public sealed class GameDb
{
    public bool Ready { get; private set; }
    public string Status { get; private set; } = "building...";

    public Dictionary<uint, QuestInfo> Quests = new();
    public List<QuestInfo> MainScenario = new();              // ordered
    public Dictionary<uint, ZoneInfo> Zones = new();
    public List<AchievementInfo> Achievements = new();
    public List<HuntTarget> Hunts = new();
    public List<DutyInfo> Duties = new();
    public Dictionary<uint, string> ExpansionNames = new();
    public Dictionary<uint, WorldPoint> NpcLocations = new();  // ENpc / EObj id -> first placement
    public Dictionary<uint, string> NpcNames = new();
    public Dictionary<uint, (uint Territory, uint Map, string Zone)> AetheryteInfo = new();
    public List<GearItem>? Gear { get; private set; }
    public bool GearBuilding { get; private set; }

    private readonly IDataManager data;
    private Dictionary<uint, List<uint>> shopToNpcs = new();

    public GameDb(IDataManager data) => this.data = data;

    public string Expansion(uint id) => ExpansionNames.TryGetValue(id, out var n) ? n : "?";
    public string ZoneName(uint territory) => Zones.TryGetValue(territory, out var z) ? z.Name : TerritoryName(territory);

    public string TerritoryName(uint territory)
    {
        var t = data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory);
        return t?.PlaceName.ValueNullable?.Name.ExtractText() ?? $"territory {territory}";
    }

    public uint MapFor(uint territory) =>
        data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.Map.RowId ?? 0;

    public void Build(QuestPathStore paths, string pluginDir)
    {
        try
        {
            Status = "expansions"; BuildExpansions();
            Status = "npc placements"; BuildLevels();
            Status = "zones"; BuildZones();
            Status = "quests"; BuildQuests(paths);
            Status = "achievements"; BuildAchievements();
            Status = "hunts"; BuildHunts(pluginDir);
            Status = "duties"; BuildDuties();
            Ready = true;
            Status = $"{Quests.Count} quests, {Zones.Count} zones, {Achievements.Count} achievements, {Hunts.Count} hunt marks, {Duties.Count} duties";
        }
        catch (Exception ex)
        {
            Status = "build failed at " + Status + ": " + ex.Message;
            Plugin.Log.Error(ex, "GameDb build failed");
        }
    }

    /// Re-applies Questionable paths after a fresh download without rebuilding everything.
    public void ApplyPaths(QuestPathStore paths)
    {
        foreach (var q in Quests.Values) AttachSteps(q, paths);
    }

    private void BuildExpansions()
    {
        foreach (var e in data.GetExcelSheet<ExVersion>())
            ExpansionNames[e.RowId] = e.Name.ExtractText();
    }

    private void BuildLevels()
    {
        foreach (var l in data.GetExcelSheet<Level>())
        {
            var obj = l.Object.RowId;
            if (obj == 0 || NpcLocations.ContainsKey(obj)) continue;
            if (l.Territory.RowId == 0) continue;
            NpcLocations[obj] = new WorldPoint(l.Territory.RowId, l.Map.RowId, new Vector3(l.X, l.Y, l.Z));
        }
        foreach (var n in data.GetExcelSheet<ENpcResident>())
        {
            var s = n.Singular.ExtractText();
            if (s.Length > 0) NpcNames[n.RowId] = Capitalize(s);
        }
        foreach (var n in data.GetExcelSheet<EObjName>())
        {
            var s = n.Singular.ExtractText();
            if (s.Length > 0) NpcNames[n.RowId] = Capitalize(s);
        }
    }

    private void BuildZones()
    {
        foreach (var t in data.GetExcelSheet<TerritoryType>())
        {
            var name = t.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (name.Length == 0 || t.Map.RowId == 0 || t.IsPvpZone) continue;
            if (t.ContentFinderCondition.RowId != 0) continue;    // instanced duties are listed under Duties
            Zones[t.RowId] = new ZoneInfo
            {
                Territory = t.RowId,
                Map = t.Map.RowId,
                Name = name,
                Region = t.PlaceNameRegion.ValueNullable?.Name.ExtractText() ?? "",
                Expansion = t.ExVersion.RowId,
                AetherCurrentSet = t.AetherCurrentCompFlgSet.RowId,
            };
        }

        // Aetherytes. Aetheryte.Level points at layout rows that aren't in the Level sheet, so positions come
        // from each map's own marker list (Map.MapMarkerRange -> MapMarker; DataType 3 = aetheryte keyed by id,
        // 4 = aethernet shard keyed by its PlaceName), in 2048-pixel map-texture coordinates. Verified against
        // Questionable's measured positions: 197/197 within 15 yalms (tools/verify_library.py).
        var markers = new Dictionary<(uint Range, byte Type, uint Key), (short X, short Y)>();
        foreach (var coll in data.GetSubrowExcelSheet<MapMarker>())
            foreach (var mk in coll)
                if (mk.DataType is 3 or 4) markers.TryAdd((mk.RowId, mk.DataType, mk.DataKey.RowId), (mk.X, mk.Y));
        var mapsByTerritory = data.GetExcelSheet<Map>().Where(m => m.TerritoryType.RowId != 0).GroupBy(m => m.TerritoryType.RowId)
                                  .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var a in data.GetExcelSheet<Aetheryte>())
        {
            if (a.Invisible) continue;
            var terr = a.Territory.RowId;
            var name = a.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (name.Length == 0) name = a.AethernetName.ValueNullable?.Name.ExtractText() ?? "";
            var candidates = new List<Map>();
            if (a.Map.ValueNullable is { } own) candidates.Add(own);
            if (mapsByTerritory.TryGetValue(terr, out var more)) candidates.AddRange(more.Where(m => m.RowId != a.Map.RowId));
            foreach (var m in candidates)
            {
                if (!markers.TryGetValue((m.MapMarkerRange, 3, a.RowId), out var px) &&
                    !markers.TryGetValue((m.MapMarkerRange, 4, a.AethernetName.RowId), out px)) continue;
                var c = m.SizeFactor / 100f;
                var world = new Vector3((px.X - 1024f) / c - m.OffsetX, 0, (px.Y - 1024f) / c - m.OffsetY);
                if (a.IsAetheryte) AetheryteInfo[a.RowId] = (terr, m.RowId, name);
                if (Zones.TryGetValue(terr, out var z) && a.IsAetheryte) z.Aetherytes.Add((a.RowId, name, new WorldPoint(terr, m.RowId, world)));
                break;
            }
        }

        // Sightseeing log (vistas)
        const uint adventureBase = 2162688;
        foreach (var adv in data.GetExcelSheet<Adventure>())
        {
            var lvl = adv.Level.ValueNullable;
            if (lvl == null) continue;
            var terr = lvl.Value.Territory.RowId;
            if (!Zones.TryGetValue(terr, out var z)) continue;
            z.Vistas.Add((adv.RowId - adventureBase, adv.Name.ExtractText(),
                new WorldPoint(terr, lvl.Value.Map.RowId, new Vector3(lvl.Value.X, lvl.Value.Y, lvl.Value.Z))));
        }

        // Aether currents: field currents are EObjs whose Data points at the AetherCurrent row.
        var eobjForCurrent = new Dictionary<uint, uint>();
        foreach (var eo in data.GetExcelSheet<EObj>())
            if (eo.Data.RowId is >= 2818048 and < 2818048 + 2000) eobjForCurrent.TryAdd(eo.Data.RowId, eo.RowId);
        foreach (var set in data.GetExcelSheet<AetherCurrentCompFlgSet>())
        {
            var terr = set.Territory.RowId;
            if (!Zones.TryGetValue(terr, out var z)) continue;
            foreach (var cur in set.AetherCurrents)
            {
                if (cur.RowId == 0) continue;
                var quest = cur.ValueNullable?.Quest.RowId ?? 0;
                WorldPoint? where = null;
                if (eobjForCurrent.TryGetValue(cur.RowId, out var eobj) && NpcLocations.TryGetValue(eobj, out var p)) where = p;
                z.AetherCurrents.Add((cur.RowId, where, quest));
            }
        }

        // Elite marks (S/A/B) per zone
        foreach (var t in data.GetExcelSheet<TerritoryType>())
        {
            if (!Zones.TryGetValue(t.RowId, out var z)) continue;
            var nmt = t.NotoriousMonsterTerritory.ValueNullable;
            if (nmt == null) continue;
            foreach (var nm in nmt.Value.NotoriousMonsters)
            {
                var m = nm.ValueNullable;
                if (m == null || m.Value.BNpcName.RowId == 0) continue;
                z.EliteMarks.Add((m.Value.BNpcName.RowId, Capitalize(m.Value.BNpcName.ValueNullable?.Singular.ExtractText() ?? "?"), m.Value.Rank));
            }
        }
    }

    private void BuildQuests(QuestPathStore paths)
    {
        foreach (var q in data.GetExcelSheet<Quest>())
        {
            if (q.RowId < 65536) continue;
            var name = q.Name.ExtractText();
            if (name.Length == 0) continue;

            var genre = q.JournalGenre.ValueNullable;
            var cat = genre?.JournalCategory.ValueNullable;
            var section = cat?.JournalSection.ValueNullable?.Name.ExtractText() ?? "";
            var info = new QuestInfo
            {
                RowId = q.RowId,
                Name = name,
                Level = q.ClassJobLevel.Count > 0 ? q.ClassJobLevel[0] : 0,
                ClassJobCategory = q.ClassJobCategory0.RowId,
                Expansion = q.Expansion.RowId,
                Genre = genre?.Name.ExtractText() ?? "",
                Section = section,
                Repeatable = q.IsRepeatable,
                Festival = q.Festival.RowId,
                GrandCompany = q.GrandCompany.RowId,
                BeastTribe = q.BeastTribe.RowId,
                PrereqAny = q.PreviousQuestJoin == 2,
                Prereqs = q.PreviousQuest.Select(r => r.RowId).Where(r => r != 0).ToArray(),
                Locks = q.QuestLock.Select(r => r.RowId).Where(r => r != 0).ToArray(),
                UnlocksInstanceContent = q.InstanceContentUnlock.RowId,
                SortKey = q.SortKey,
                RewardItems = q.Reward.Select(r => r.RowId).Where(r => r > 0 && r < 2000000).ToArray(),
            };
            info.Kind = Classify(info, section, cat?.Name.ExtractText() ?? "");

            var issuer = q.IssuerLocation.ValueNullable;
            if (issuer != null)
                info.Start = new WorldPoint(issuer.Value.Territory.RowId, issuer.Value.Map.RowId,
                    new Vector3(issuer.Value.X, issuer.Value.Y, issuer.Value.Z));
            info.StartNpcId = q.IssuerStart.RowId;
            if (NpcNames.TryGetValue(q.IssuerStart.RowId, out var npc)) info.StartNpc = npc;

            // Game-data fallback steps: each TodoParams entry is a sequence with 0..n locations.
            foreach (var todo in q.TodoParams)
            {
                if (todo.ToDoCompleteSeq == 0 && todo.ToDoLocation.All(l => l.RowId == 0)) continue;
                foreach (var loc in todo.ToDoLocation)
                {
                    var l = loc.ValueNullable;
                    if (l == null || l.Value.Territory.RowId == 0) continue;
                    info.Steps.Add(new QuestStep
                    {
                        Sequence = todo.ToDoCompleteSeq,
                        Action = "Objective",
                        Text = "Objective area",
                        Where = new WorldPoint(l.Value.Territory.RowId, l.Value.Map.RowId, new Vector3(l.Value.X, l.Value.Y, l.Value.Z)),
                        Radius = Math.Max(3f, l.Value.Radius),
                    });
                }
            }
            if (NpcLocations.TryGetValue(q.TargetEnd.RowId, out var endAt))
                info.Steps.Add(new QuestStep
                {
                    Sequence = 255, Action = "CompleteQuest", Where = endAt, DataId = q.TargetEnd.RowId,
                    Text = "Turn in to " + (NpcNames.TryGetValue(q.TargetEnd.RowId, out var en) ? en : "the quest NPC"),
                });

            AttachSteps(info, paths);
            Quests[info.RowId] = info;
            if (info.Start.IsValid && Zones.TryGetValue(info.Start.Territory, out var z)) z.Quests.Add(info.RowId);
        }

        MainScenario = OrderChain(Quests.Values.Where(q => q.Kind == QuestKind.MainScenario).ToList());
    }

    private void AttachSteps(QuestInfo info, QuestPathStore paths)
    {
        if (!paths.Paths.TryGetValue(info.ShortId, out var qp) || qp.Count == 0) return;
        var steps = new List<QuestStep>();
        foreach (var s in qp)
        {
            if (!s.HasPos && s.Action is not ("AttuneAetheryte" or "AttuneAethernetShard" or "Duty" or "SinglePlayerDuty" or "Instruction")) continue;
            var where = s.HasPos ? new WorldPoint(s.Territory, MapFor(s.Territory), QuestPathStore.Pos(s)) : default;
            steps.Add(new QuestStep
            {
                Sequence = s.Seq, Action = s.Action, Where = where, DataId = s.DataId, AetherCurrentId = s.AetherCurrentId,
                Fly = s.Fly, Radius = s.Stop > 0 ? s.Stop : 3f, FromQuestionable = true,
                Text = Describe(s),
            });
        }
        if (steps.Count == 0) return;
        info.Steps = steps;
        info.HasDetailedPath = true;
    }

    private string Describe(QuestPathStore.Step s)
    {
        var who = s.DataId != 0 && NpcNames.TryGetValue(s.DataId, out var n) ? n : null;
        var verb = s.Action switch
        {
            "AcceptQuest" => who != null ? $"Accept the quest from {who}" : "Accept the quest",
            "CompleteQuest" => who != null ? $"Turn in to {who}" : "Turn in the quest",
            "Interact" => who != null ? $"Talk to / use {who}" : "Interact here",
            "WalkTo" => "Go here",
            "Combat" => "Defeat the enemies here",
            "UseItem" => who != null ? $"Use the quest item on {who}" : "Use the quest item here",
            "AttuneAetheryte" => "Attune to the aetheryte",
            "AttuneAethernetShard" => "Attune to the aethernet shard",
            "AttuneAetherCurrent" => "Attune to the aether current",
            "Emote" => who != null ? $"Use the requested emote on {who}" : "Use the requested emote",
            "Duty" or "SinglePlayerDuty" => "Enter and clear the duty",
            "Craft" => "Craft the requested item",
            "Gather" => "Gather the requested items",
            "PurchaseItem" => "Buy the requested item",
            "Jump" => "Jump across here",
            "Say" => "Say the requested phrase in /say",
            "EquipItem" => "Equip the reward item",
            "Snipe" => "Snipe the target(s)",
            "Fish" => "Fish here",
            "Dive" => "Dive here",
            _ => s.Action,
        };
        return s.Comment.Length > 0 ? $"{verb} - {s.Comment}" : verb;
    }

    private static QuestKind Classify(QuestInfo q, string section, string category)
    {
        if (q.Festival != 0) return QuestKind.Seasonal;
        var s = section.ToLowerInvariant();
        var c = category.ToLowerInvariant();
        if (s.Contains("main scenario")) return QuestKind.MainScenario;
        if (s.Contains("chronicles") || s.Contains("feature")) return QuestKind.Feature;
        if (q.BeastTribe != 0 || c.Contains("allied") || c.Contains("beast") || c.Contains("tribal")) return QuestKind.AlliedSociety;
        if (s.Contains("class") || s.Contains("job") || c.Contains("role quest")) return QuestKind.ClassJob;
        if (q.Repeatable) return QuestKind.Repeatable;
        if (s.Length > 0) return QuestKind.Side;
        return QuestKind.Other;
    }

    /// Topological order through PreviousQuest, tie-broken by expansion then SortKey.
    private static List<QuestInfo> OrderChain(List<QuestInfo> quests)
    {
        var byId = quests.ToDictionary(q => q.RowId);
        var depth = new Dictionary<uint, int>();
        int Depth(uint id, int guard)
        {
            if (depth.TryGetValue(id, out var d)) return d;
            if (guard > 2000 || !byId.TryGetValue(id, out var q)) return 0;
            depth[id] = 0;
            var best = 0;
            foreach (var p in q.Prereqs) if (byId.ContainsKey(p)) best = Math.Max(best, Depth(p, guard + 1) + 1);
            depth[id] = best;
            return best;
        }
        foreach (var q in quests) Depth(q.RowId, 0);
        return quests.OrderBy(q => q.Expansion).ThenBy(q => depth[q.RowId]).ThenBy(q => q.SortKey).ThenBy(q => q.RowId).ToList();
    }

    private void BuildAchievements()
    {
        var quests = data.GetExcelSheet<Quest>();
        foreach (var a in data.GetExcelSheet<Achievement>())
        {
            var name = a.Name.ExtractText();
            if (name.Length == 0) continue;
            var cat = a.AchievementCategory.ValueNullable;
            var linked = a.Type == 6
                ? a.Data.Select(d => d.RowId).Where(id => id >= 65536 && quests.HasRow(id)).ToArray()
                : [];
            if (a.Type == 6 && linked.Length == 0 && a.Key.RowId >= 65536 && quests.HasRow(a.Key.RowId))
                linked = [a.Key.RowId];
            Achievements.Add(new AchievementInfo
            {
                Id = a.RowId, Name = name, Description = a.Description.ExtractText(),
                Category = cat?.Name.ExtractText() ?? "",
                Kind = cat?.AchievementKind.ValueNullable?.Name.ExtractText() ?? "",
                Points = a.Points, Type = a.Type, LinkedQuests = linked,
                RewardItem = a.Item.RowId, Title = a.Title.RowId,
            });
        }
    }

    private void BuildHunts(string pluginDir)
    {
        var orders = data.GetSubrowExcelSheet<MobHuntOrder>();
        foreach (var type in data.GetExcelSheet<MobHuntOrderType>())
        {
            if (type.OrderAmount == 0) continue;
            var start = type.OrderStart.RowId;
            var billName = type.EventItem.ValueNullable?.Name.ExtractText() ?? $"Hunt bill {type.RowId}";
            for (uint r = start; r < start + type.OrderAmount; r++)
            {
                if (!orders.TryGetRow(r, out var subrows)) continue;
                byte mob = 0;
                foreach (var o in subrows)
                {
                    var target = o.Target.ValueNullable;
                    if (target == null) { mob++; continue; }
                    var map = target.Value.Map.ValueNullable;
                    var terr = map?.TerritoryType.RowId ?? 0;
                    Hunts.Add(new HuntTarget
                    {
                        MarkIndex = (byte)type.RowId, OrderRow = r, MobIndex = mob++,
                        BillName = Capitalize(billName), NameId = target.Value.Name.RowId,
                        Name = Capitalize(target.Value.Name.ValueNullable?.Singular.ExtractText() ?? "?"),
                        NeededKills = o.NeededKills, Elite = type.Type == 2,
                        Territory = terr, Map = map?.RowId ?? 0,
                        Zone = map?.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
                    });
                }
            }
        }

        // HuntHelper (MIT) spawn points for S/A/B elite marks, in map coordinates.
        var spawnFile = Path.Combine(pluginDir, "Data", "SpawnPointData.json");
        if (!File.Exists(spawnFile)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(spawnFile));
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var terr = m.GetProperty("MapID").GetUInt32();
            if (!Zones.TryGetValue(terr, out var z)) continue;
            foreach (var p in m.GetProperty("Positions").EnumerateArray())
            {
                var w = MapMath.MapCoordToWorld(z.Map, p.GetProperty("X").GetSingle(), p.GetProperty("Y").GetSingle(), data);
                z.HuntSpawnPoints.Add(new WorldPoint(terr, z.Map, w));
            }
        }
    }

    private void BuildDuties()
    {
        var unlockBy = new Dictionary<uint, uint>();
        foreach (var q in Quests.Values)
            if (q.UnlocksInstanceContent != 0) unlockBy.TryAdd(q.UnlocksInstanceContent, q.RowId);
        foreach (var c in data.GetExcelSheet<ContentFinderCondition>())
        {
            var name = c.Name.ExtractText();
            if (name.Length == 0 || c.PvP) continue;
            var ic = c.ContentLinkType == 1 ? c.Content.RowId : 0u;
            Duties.Add(new DutyInfo
            {
                CfcId = c.RowId, Name = Capitalize(name),
                ContentType = c.ContentType.ValueNullable?.Name.ExtractText() ?? "",
                Level = c.ClassJobLevelRequired, ItemLevel = c.ItemLevelRequired,
                Expansion = c.RequiredExVersion.RowId, InstanceContentId = ic,
                UnlockQuest = ic != 0 && unlockBy.TryGetValue(ic, out var uq) ? uq : 0,
            });
        }
        Duties = Duties.OrderBy(d => d.Expansion).ThenBy(d => d.Level).ThenBy(d => d.Name).ToList();
    }

    /// Gear index is big (~40k items); built on demand the first time the Gear tab opens.
    public void BuildGear()
    {
        if (Gear != null || GearBuilding) return;
        GearBuilding = true;
        try
        {
            // Shop -> NPC placement
            foreach (var npc in data.GetExcelSheet<ENpcBase>())
                foreach (var d in npc.ENpcData)
                    if (d.RowId != 0)
                    {
                        if (!shopToNpcs.TryGetValue(d.RowId, out var l)) shopToNpcs[d.RowId] = l = new();
                        if (l.Count < 4) l.Add(npc.RowId);
                    }

            var sources = new Dictionary<uint, List<string>>();
            var points = new Dictionary<uint, List<WorldPoint>>();
            void Add(uint item, string src, uint? shop = null)
            {
                if (!sources.TryGetValue(item, out var l)) sources[item] = l = new();
                if (l.Count < 8 && !l.Contains(src)) l.Add(src);
                if (shop is { } s && shopToNpcs.TryGetValue(s, out var npcs))
                    foreach (var n in npcs)
                        if (NpcLocations.TryGetValue(n, out var p))
                        {
                            if (!points.TryGetValue(item, out var pl)) points[item] = pl = new();
                            if (pl.Count < 4) pl.Add(p);
                        }
            }

            foreach (var coll in data.GetSubrowExcelSheet<GilShopItem>())
                foreach (var gi in coll)
                    if (gi.Item.RowId != 0) Add(gi.Item.RowId, "Gil shop" + NpcSuffix(gi.RowId), gi.RowId);
            foreach (var sp in data.GetExcelSheet<SpecialShop>())
                foreach (var entry in sp.Item)
                    foreach (var rec in entry.ReceiveItems)
                        if (rec.Item.RowId != 0)
                        {
                            var cost = entry.ItemCosts.Count > 0 ? entry.ItemCosts[0].ItemCost.ValueNullable?.Name.ExtractText() : null;
                            Add(rec.Item.RowId, "Exchange" + (string.IsNullOrEmpty(cost) ? "" : $" ({cost})") + NpcSuffix(sp.RowId), sp.RowId);
                        }
            var craftNames = new[] { "Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian" };
            foreach (var r in data.GetExcelSheet<Recipe>())
                if (r.ItemResult.RowId != 0)
                    Add(r.ItemResult.RowId, "Crafted: " + (r.CraftType.RowId < craftNames.Length ? craftNames[r.CraftType.RowId] : "crafter")
                                             + $" lv {r.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0}");
            var fromQuest = new Dictionary<uint, uint>();
            foreach (var q in Quests.Values)
                foreach (var it in q.RewardItems)
                {
                    fromQuest.TryAdd(it, q.RowId);
                    Add(it, "Quest reward: " + q.Name);
                }
            var fromAch = new Dictionary<uint, uint>();
            foreach (var a in Achievements)
                if (a.RewardItem != 0) { fromAch.TryAdd(a.RewardItem, a.Id); Add(a.RewardItem, "Achievement: " + a.Name); }

            var list = new List<GearItem>();
            foreach (var it in data.GetExcelSheet<Item>())
            {
                var slot = it.EquipSlotCategory.ValueNullable;
                if (slot == null || it.EquipSlotCategory.RowId == 0) continue;
                var name = it.Name.ExtractText();
                if (name.Length == 0) continue;
                var g = new GearItem
                {
                    Id = it.RowId, Name = name, ItemLevel = (int)it.LevelItem.RowId, EquipLevel = it.LevelEquip,
                    Slot = SlotName(slot.Value), Category = it.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "",
                    Jobs = it.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? "", JobCategory = it.ClassJobCategory.RowId,
                    FromQuest = fromQuest.GetValueOrDefault(it.RowId), FromAchievement = fromAch.GetValueOrDefault(it.RowId),
                };
                if (sources.TryGetValue(it.RowId, out var s)) g.Sources = s;
                else g.Sources.Add(it.IsUntradable ? "Duty drop / other (untradable)" : "Duty drop / market board / other");
                if (points.TryGetValue(it.RowId, out var p)) g.SourcePoints = p;
                list.Add(g);
            }
            Gear = list.OrderBy(g => g.Slot).ThenByDescending(g => g.ItemLevel).ToList();
        }
        catch (Exception ex) { Plugin.Log.Error(ex, "gear build failed"); Gear = new(); }
        finally { GearBuilding = false; }
    }

    private string NpcSuffix(uint shopId)
    {
        if (!shopToNpcs.TryGetValue(shopId, out var npcs)) return "";
        foreach (var n in npcs)
            if (NpcNames.TryGetValue(n, out var name) && NpcLocations.TryGetValue(n, out var p))
                return $" - {name}, {ZoneName(p.Territory)}";
        return "";
    }

    private static string SlotName(EquipSlotCategory s) =>
        s.MainHand > 0 ? "Main hand" : s.OffHand > 0 ? "Off hand" : s.Head > 0 ? "Head" : s.Body > 0 ? "Body" :
        s.Gloves > 0 ? "Hands" : s.Waist > 0 ? "Waist" : s.Legs > 0 ? "Legs" : s.Feet > 0 ? "Feet" :
        s.Ears > 0 ? "Ears" : s.Neck > 0 ? "Neck" : s.Wrists > 0 ? "Wrists" : (s.FingerL > 0 || s.FingerR > 0) ? "Ring" :
        s.SoulCrystal > 0 ? "Soul crystal" : "Other";

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
