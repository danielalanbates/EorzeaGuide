// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using EorzeaGuide.Data;
using EorzeaGuide.Nav;

namespace EorzeaGuide.Planning;

public enum ObjKind { Quest, Aetheryte, AetherCurrent, Vista, EliteSpawn, HuntMark, Travel, Info }
public enum GuideMode { Leveling, Zone, Quest, Achievement, Hunt, EliteSweep }

public sealed class Objective
{
    public ObjKind Kind;
    public string Title = "";
    public string Detail = "";
    public WorldPoint Where;
    public uint QuestId;
    public uint Id;
    public bool Fly;
    public float Radius = 4f;
    public string Key = "";
    public uint TravelAetheryte;
    public string TravelAetheryteName = "";
    public float EtaSeconds;
    public uint DataId;                 // ENpc/EObj the step targets, for the over-head beacon
}

/// The Zygor brain: turns the character's state plus a mode into an ordered queue of
/// objectives. Current = Queue[0]; the overlay draws the road to it.
public sealed class Planner
{
    private readonly IPlannerHost plugin;
    public List<Objective> Queue { get; private set; } = new();
    public Objective? Current => Queue.Count > 0 ? Queue[0] : null;
    public string Headline { get; private set; } = "";

    private readonly Dictionary<uint, (byte Seq, int Step)> stepCursor = new();
    private DateTime lastPlan = DateTime.MinValue;
    private DateTime nearSince = DateTime.MaxValue;
    private string nearKey = "";

    public Planner(IPlannerHost host) => plugin = host;

    private Configuration Cfg => plugin.Config;
    private GameDb Db => plugin.Db;

    public void ForceReplan() => lastPlan = DateTime.MinValue;

    public void Skip(Objective o)
    {
        if (o.Kind == ObjKind.Quest && o.QuestId != 0 && Progress.QuestAccepted(o.QuestId))
        {
            var seq = Progress.QuestSequence(o.QuestId);
            var cur = stepCursor.TryGetValue(o.QuestId, out var c) && c.Seq == seq ? c.Step : 0;
            stepCursor[o.QuestId] = (seq, cur + 1);
        }
        else Cfg.Skipped.Add(o.Key);
        plugin.SaveConfig();
        ForceReplan();
    }

    public void Tick(Vector3 player, uint territory)
    {
        if (!Db.Ready) return;
        AutoAdvanceSteps(player, territory);
        if ((plugin.Now - lastPlan).TotalSeconds < 2) return;
        lastPlan = plugin.Now;
        try { Queue = Plan(player, territory); }
        catch (Exception ex) { plugin.LogError(ex, "plan failed"); }
    }

    /// Steps inside one quest sequence advance when you stand at them for 2 seconds.
    private void AutoAdvanceSteps(Vector3 player, uint territory)
    {
        var cur = Current;
        if (cur == null || cur.Kind != ObjKind.Quest || !cur.Where.IsValid || cur.Where.Territory != territory) { nearSince = DateTime.MaxValue; return; }
        var close = MapMath.Flat(player, cur.Where.Pos) <= Math.Max(cur.Radius, 3f) + 1.5f;
        if (!close) { nearSince = DateTime.MaxValue; return; }
        if (nearKey != cur.Key) { nearKey = cur.Key; nearSince = plugin.Now; return; }
        if ((plugin.Now - nearSince).TotalSeconds < 2 || !Progress.QuestAccepted(cur.QuestId)) return;
        var seq = Progress.QuestSequence(cur.QuestId);
        var q = Db.Quests[cur.QuestId];
        var inSeq = q.Steps.Count(s => s.Sequence == seq && s.Where.IsValid);
        var idx = stepCursor.TryGetValue(cur.QuestId, out var c) && c.Seq == seq ? c.Step : 0;
        if (idx + 1 < inSeq) { stepCursor[cur.QuestId] = (seq, idx + 1); nearSince = DateTime.MaxValue; ForceReplan(); }
    }

    private List<Objective> Plan(Vector3 player, uint territory)
    {
        var list = new List<Objective>();
        switch (Cfg.Mode)
        {
            case GuideMode.Leveling: PlanLeveling(list, territory); break;
            case GuideMode.Zone: PlanZone(list, Cfg.FocusZone == 0 ? territory : Cfg.FocusZone, true); break;
            case GuideMode.Quest: PlanQuest(list, Cfg.FocusQuest); break;
            case GuideMode.Achievement: PlanAchievement(list); break;
            case GuideMode.Hunt: PlanHunt(list); break;
            case GuideMode.EliteSweep: PlanElite(list, Cfg.FocusZone == 0 ? territory : Cfg.FocusZone); break;
        }
        list.RemoveAll(o => Cfg.Skipped.Contains(o.Key));
        return RouteAndTravel(list, player, territory);
    }

    /// Same-zone objectives are routed as the shortest tour; other zones follow, as travel legs.
    private List<Objective> RouteAndTravel(List<Objective> list, Vector3 player, uint territory)
    {
        var here = list.Where(o => o.Where.IsValid && o.Where.Territory == territory).ToList();
        var pinned = Cfg.Mode == GuideMode.Quest ? here : null;
        var rest = list.Where(o => !here.Contains(o)).ToList();
        List<Objective> ordered;
        if (pinned != null) ordered = here;
        else
        {
            var order = RouteOptimizer.Order(player, here.Select(o => o.Where.Pos).ToList());
            ordered = order.Select(i => here[i]).ToList();
        }
        ordered.AddRange(rest);
        foreach (var o in ordered.Take(1))
        {
            if (!o.Where.IsValid) continue;
            var plan = TravelRouter.Choose(Db, territory, player, o.Where);
            o.EtaSeconds = plan.Seconds;
            if (plan.Teleport) { o.TravelAetheryte = plan.Aetheryte; o.TravelAetheryteName = plan.AetheryteName; }
        }
        foreach (var o in rest)
            if (o.Where.IsValid && o.Where.Territory != territory)
            {
                var ae = o.TravelAetheryte != 0 ? (o.TravelAetheryte, o.TravelAetheryteName) : NearestAetheryte(o.Where);
                o.TravelAetheryte = ae.Item1; o.TravelAetheryteName = ae.Item2;
                o.Detail = $"In {Db.ZoneName(o.Where.Territory)}" + (ae.Item1 != 0 ? $" - teleport to {ae.Item2}" : " - no attuned aetheryte there yet") +
                           (o.Detail.Length > 0 ? ". " + o.Detail : "");
            }
        Headline = Cfg.Mode switch
        {
            GuideMode.Leveling => $"Leveling guide - {here.Count} objectives in this zone",
            GuideMode.Zone => $"Zone completion: {Db.ZoneName(Cfg.FocusZone == 0 ? territory : Cfg.FocusZone)} - {list.Count} left",
            GuideMode.Quest => Db.Quests.TryGetValue(Cfg.FocusQuest, out var fq) ? $"Quest: {fq.Name}" : "Quest",
            GuideMode.Achievement => $"Achievement: {Db.Achievements.FirstOrDefault(a => a.Id == Cfg.FocusAchievement)?.Name}",
            GuideMode.Hunt => "Hunt bill targets",
            GuideMode.EliteSweep => $"Elite mark spawn sweep - {list.Count} points",
            _ => "",
        };
        return ordered;
    }

    public (uint Id, string Name) NearestAetheryte(WorldPoint p)
    {
        (uint, string) best = (0, "");
        var bd = float.MaxValue;
        if (!Db.Zones.TryGetValue(p.Territory, out var z)) return best;
        foreach (var a in z.Aetherytes)
        {
            if (!Progress.Aetheryte(a.Id)) continue;
            var d = Vector3.Distance(a.Where.Pos, p.Pos);
            if (d < bd) { bd = d; best = (a.Id, a.Name); }
        }
        return best;
    }

    // ---------- modes ----------

    private void PlanLeveling(List<Objective> list, uint territory)
    {
        // 1. Everything already in the journal.
        var accepted = new HashSet<uint>();
        foreach (var id in Progress.State.AcceptedQuests())
        {
            if (!Db.Quests.TryGetValue(id, out var q)) continue;
            accepted.Add(id);
            if (QuestObjective(q) is { } o) list.Add(o);
        }

        // 2. Next main scenario quest.
        var msq = Db.MainScenario.FirstOrDefault(q => !Progress.QuestDone(q.RowId) && !accepted.Contains(q.RowId) && Progress.Available(q, true));
        if (msq != null && QuestObjective(msq) is { } mo)
        {
            if (msq.Level > Progress.Level) mo.Detail = $"Main scenario needs level {msq.Level} (you are {Progress.Level}). Do the side quests below first.";
            mo.Title = "[MSQ] " + mo.Title;
            list.Add(mo);
        }

        // 3. Sweep: available side, feature and job quests that start in this zone.
        var zone = Db.Zones.GetValueOrDefault(territory);
        foreach (var id in zone?.Quests ?? new List<uint>())
        {
            var q = Db.Quests[id];
            if (accepted.Contains(id) || q == msq) continue;
            if (!SweepKind(q)) continue;
            if (!Progress.Available(q)) continue;
            if (QuestObjective(q) is { } o) list.Add(o);
        }

        // 4. Zone collectibles: aetherytes, vistas, aether currents.
        if (zone != null) AddZoneCollectibles(list, zone, includeHunts: false);
    }

    private bool SweepKind(QuestInfo q) => q.Kind switch
    {
        QuestKind.Side => Cfg.SweepSideQuests,
        QuestKind.Feature => true,
        QuestKind.ClassJob => true,
        QuestKind.AlliedSociety => Cfg.SweepAlliedSocieties,
        QuestKind.Repeatable => false,
        QuestKind.Seasonal => false,
        _ => Cfg.SweepSideQuests,
    };

    private void PlanZone(List<Objective> list, uint territory, bool includeHunts)
    {
        if (!Db.Zones.TryGetValue(territory, out var zone)) return;
        foreach (var id in zone.Quests)
        {
            var q = Db.Quests[id];
            if (q.Kind is QuestKind.Repeatable or QuestKind.Seasonal) continue;
            if (Progress.QuestDone(id)) continue;
            if (!Progress.QuestAccepted(id) && !Progress.Available(q, Cfg.ZoneIgnoresLevel)) continue;
            if (QuestObjective(q) is { } o) list.Add(o);
        }
        AddZoneCollectibles(list, zone, includeHunts);
    }

    private void AddZoneCollectibles(List<Objective> list, ZoneInfo zone, bool includeHunts)
    {
        if (Cfg.ShowAetherytes)
            foreach (var a in zone.Aetherytes)
                if (!Progress.Aetheryte(a.Id))
                    list.Add(new Objective { Kind = ObjKind.Aetheryte, Title = $"Attune to {a.Name} aetheryte", Where = a.Where, Id = a.Id, Radius = 8, Key = $"ae{a.Id}" });
        if (Cfg.ShowVistas)
            foreach (var v in zone.Vistas)
                if (!Progress.Vista(v.Index))
                    list.Add(new Objective { Kind = ObjKind.Vista, Title = $"Sightseeing: {v.Name}", Detail = "Use the listed emote at the vista (check the in-game Sightseeing Log for time/weather).", Where = v.Where, Id = v.Index, Key = $"vi{v.Index}" });
        if (Cfg.ShowAetherCurrents)
            foreach (var c in zone.AetherCurrents)
            {
                if (Progress.AetherCurrent(c.Id)) continue;
                if (c.Quest != 0)
                {
                    if (Db.Quests.TryGetValue(c.Quest, out var cq) && !Progress.QuestDone(c.Quest) && Progress.Available(cq, true)
                        && list.All(o => o.QuestId != c.Quest) && QuestObjective(cq) is { } o)
                    { o.Title = "[Aether current] " + o.Title; list.Add(o); }
                    continue;
                }
                if (c.Where is { } w)
                    list.Add(new Objective { Kind = ObjKind.AetherCurrent, Title = "Attune to aether current", Where = w, Id = c.Id, Key = $"ac{c.Id}" });
            }
        if (includeHunts)
            foreach (var h in Db.Hunts.Where(h => h.Territory == zone.Territory))
                if (Progress.HuntOrderHeld(h.MarkIndex) == h.OrderRow && Progress.HuntKills(h.MarkIndex, h.MobIndex) < h.NeededKills)
                    list.Add(HuntObjective(h));
    }

    private void PlanQuest(List<Objective> list, uint questId)
    {
        if (!Db.Quests.TryGetValue(questId, out var q)) return;
        // Walk back through unmet prerequisites to the first thing you can actually do.
        var guard = 0;
        while (!Progress.QuestAccepted(q.RowId) && !Progress.Available(q, true) && guard++ < 400)
        {
            var next = q.Prereqs.Select(p => Db.Quests.GetValueOrDefault(p)).FirstOrDefault(p => p != null && !Progress.QuestDone(p.RowId));
            if (next == null) break;
            q = next;
        }
        if (Progress.QuestDone(questId)) { list.Add(new Objective { Kind = ObjKind.Info, Title = "Quest complete!", Key = "done" }); return; }
        if (QuestObjective(q) is { } o)
        {
            if (q.RowId != questId) o.Detail = $"Unlocks the path to {Db.Quests[questId].Name}. " + o.Detail;
            list.Add(o);
        }
    }

    private void PlanAchievement(List<Objective> list)
    {
        var a = Db.Achievements.FirstOrDefault(x => x.Id == Cfg.FocusAchievement);
        if (a == null) return;
        if (Progress.Achievement(a.Id) == true) { list.Add(new Objective { Kind = ObjKind.Info, Title = "Achievement complete!", Key = "done" }); return; }
        foreach (var qid in a.LinkedQuests)
        {
            if (Progress.QuestDone(qid) || !Db.Quests.TryGetValue(qid, out var q)) continue;
            var sub = new List<Objective>();
            var saved = Cfg.FocusQuest;
            Cfg.FocusQuest = qid;
            PlanQuest(sub, qid);
            Cfg.FocusQuest = saved;
            list.AddRange(sub);
        }
        if (list.Count == 0)
            list.Add(new Objective { Kind = ObjKind.Info, Title = a.Name, Detail = a.Description, Key = $"ach{a.Id}" });
    }

    private void PlanHunt(List<Objective> list)
    {
        foreach (var h in Db.Hunts)
        {
            if (Cfg.FocusHuntMark != 0 && h.MarkIndex != Cfg.FocusHuntMark) continue;
            if (Progress.HuntOrderHeld(h.MarkIndex) != h.OrderRow) continue;
            if (Progress.HuntKills(h.MarkIndex, h.MobIndex) >= h.NeededKills) continue;
            list.Add(HuntObjective(h));
        }
        if (list.Count == 0)
            list.Add(new Objective { Kind = ObjKind.Info, Title = "No open hunt bill targets", Detail = "Pick up a Mark Bill or Clan Mark Log from the hunt board, then its targets appear here.", Key = "hunt-none" });
    }

    private void PlanElite(List<Objective> list, uint territory)
    {
        if (!Db.Zones.TryGetValue(territory, out var z)) return;
        var i = 0;
        foreach (var p in z.HuntSpawnPoints)
            list.Add(new Objective { Kind = ObjKind.EliteSpawn, Title = $"Check spawn point {++i}", Detail = string.Join(", ", z.EliteMarks.Select(m => $"{"?BAS"[Math.Clamp(m.Rank, 0, 3)]}: {m.Name}")), Where = p, Fly = true, Radius = 25, Key = $"sp{territory}-{i}" });
    }

    private Objective HuntObjective(HuntTarget h)
    {
        var kills = Progress.HuntKills(h.MarkIndex, h.MobIndex);
        var where = plugin.LearnedPosition(h.NameId) ?? default;
        if (!where.IsValid && Db.Zones.TryGetValue(h.Territory, out var z) && z.Aetherytes.Count > 0) where = z.Aetherytes[0].Where;
        return new Objective
        {
            Kind = ObjKind.HuntMark, Title = $"Hunt {h.Name} ({kills}/{h.NeededKills})",
            Detail = $"{h.BillName} - {h.Zone}" + (plugin.LearnedPosition(h.NameId) == null ? " (exact spot learned once you see one)" : ""),
            Where = where, Id = h.NameId, Radius = 15, Key = $"hm{h.MarkIndex}-{h.OrderRow}-{h.MobIndex}",
        };
    }

    /// Where to go next for a quest: its current step if accepted, otherwise the quest giver.
    public Objective? QuestObjective(QuestInfo q)
    {
        if (Progress.QuestAccepted(q.RowId))
        {
            var seq = Progress.QuestSequence(q.RowId);
            var steps = q.Steps.Where(s => s.Sequence == seq && s.Where.IsValid).ToList();
            if (steps.Count == 0) steps = q.Steps.Where(s => s.Sequence == 255 && s.Where.IsValid).ToList();
            var idx = stepCursor.TryGetValue(q.RowId, out var c) && c.Seq == seq ? c.Step : 0;
            if (steps.Count == 0)
                return new Objective { Kind = ObjKind.Quest, QuestId = q.RowId, Title = q.Name, Detail = "No mapped location for this step - follow the quest's own map marker.", Key = $"q{q.RowId}-{seq}" };
            var st = steps[Math.Min(idx, steps.Count - 1)];
            return new Objective
            {
                Kind = ObjKind.Quest, QuestId = q.RowId, Title = $"{q.Name}: {st.Text}",
                Detail = (st.FromQuestionable ? "" : "approximate area from game data. ") + (steps.Count > 1 ? $"step {Math.Min(idx, steps.Count - 1) + 1}/{steps.Count} of this part" : ""),
                Where = st.Where, Fly = st.Fly, Radius = st.Radius, DataId = st.DataId, Key = $"q{q.RowId}-{seq}-{idx}",
            };
        }
        if (!q.Start.IsValid) return null;
        return new Objective
        {
            Kind = ObjKind.Quest, QuestId = q.RowId,
            Title = $"Pick up {q.Name}" + (q.StartNpc.Length > 0 ? $" from {q.StartNpc}" : ""),
            Detail = $"Lv {q.Level} {q.Kind}", Where = q.Start, Radius = 4, DataId = q.StartNpcId, Key = $"q{q.RowId}-start",
        };
    }
}
