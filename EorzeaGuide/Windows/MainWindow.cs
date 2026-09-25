// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EorzeaGuide.Data;
using EorzeaGuide.Planning;

namespace EorzeaGuide.Windows;

/// Completionist-style dashboard: every category shows done/total with a bar, then a
/// searchable checklist grouped by expansion; every row has a "Guide me" button.
public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private string search = "";
    private int questKindFilter = -1;
    private bool hideDone = true;
    private string gearSlot = "All";
    private int gearMinIlvl, gearMaxIlvl = 999;
    private bool gearMyJob = true;
    private string achCategory = "";

    // Completion snapshots (rebuilt on Refresh so the UI never scans 40k rows per frame)
    private Dictionary<uint, bool> questDone = new();
    private Dictionary<uint, bool?> achDone = new();
    private DateTime snapAt = DateTime.MinValue;

    private static readonly Vector4 Green = new(0.4f, 0.95f, 0.4f, 1);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.2f, 1);

    public MainWindow(Plugin plugin) : base("EorzeaGuide###EorzeaGuideMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(520, 360), MaximumSize = new Vector2(1600, 1400) };
        Size = new Vector2(760, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private Configuration Cfg => plugin.Config;
    private GameDb Db => plugin.Db;

    public override void Draw()
    {
        if (!Db.Ready)
        {
            ImGui.TextWrapped("Building guide data from the game files: " + Db.Status);
            ImGui.TextDisabled("Quest paths: " + plugin.QuestPaths.Status);
            return;
        }
        if (Plugin.ClientState.IsLoggedIn && (DateTime.UtcNow - snapAt).TotalSeconds > 15) Snapshot();

        if (ImGui.BeginTabBar("##egtabs"))
        {
            Tab("Guide", DrawGuide);
            Tab("Overview", DrawOverview);
            Tab("Zones", DrawZones);
            Tab("Quests", DrawQuests);
            Tab("Achievements", DrawAchievements);
            Tab("Hunts", DrawHunts);
            Tab("Duties", DrawDuties);
            Tab("Gear", DrawGear);
            Tab("Custom", DrawCustom);
            Tab("Settings", DrawSettings);
            ImGui.EndTabBar();
        }
    }

    private static void Tab(string name, Action body)
    {
        if (!ImGui.BeginTabItem(name)) return;
        body();
        ImGui.EndTabItem();
    }

    private void Snapshot()
    {
        snapAt = DateTime.UtcNow;
        questDone = Db.Quests.Keys.ToDictionary(k => k, Progress.QuestDone);
        achDone = Db.Achievements.ToDictionary(a => a.Id, a => Progress.Achievement(a.Id));
    }

    private bool QDone(uint id) => questDone.TryGetValue(id, out var d) && d;

    private void Guide(GuideMode mode, Action? set = null)
    {
        set?.Invoke();
        Cfg.Mode = mode;
        Cfg.Skipped.Clear();
        plugin.SaveConfig();
        plugin.Planner.ForceReplan();
        plugin.SetStepWindow(true);
    }

    private static void Bar(string label, int done, int total)
    {
        var frac = total > 0 ? done / (float)total : 0;
        ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{label}  {done}/{total}  ({frac * 100:0.0}%)");
    }

    // ---------------- Guide ----------------
    private void DrawGuide()
    {
        ImGui.TextWrapped("Pick what the arrow should lead you through. The step window and the on-screen road follow the first objective; objectives in your zone are ordered as the shortest route.");
        ImGui.Spacing();
        if (ImGui.Button("Leveling guide (MSQ + zone sweep)")) Guide(GuideMode.Leveling);
        ImGui.SameLine();
        if (ImGui.Button("Complete this zone")) Guide(GuideMode.Zone, () => Cfg.FocusZone = 0);
        ImGui.SameLine();
        if (ImGui.Button("Hunt bill targets")) Guide(GuideMode.Hunt, () => Cfg.FocusHuntMark = 0);
        ImGui.SameLine();
        if (ImGui.Button("S/A/B spawn sweep here")) Guide(GuideMode.EliteSweep, () => Cfg.FocusZone = 0);

        ImGui.Separator();
        ImGui.TextColored(Gold, $"Mode: {Cfg.Mode} - {plugin.Planner.Headline}");
        ImGui.TextDisabled("Pathing: " + (plugin.Nav.NavmeshReady ? "vnavmesh (obstacle-aware roads)" : "vnavmesh not installed - straight-line arrow only"));
        if (ImGui.BeginChild("##queue", new Vector2(-1, -1), true))
        {
            var i = 0;
            foreach (var o in plugin.Planner.Queue.Take(60))
            {
                ImGui.PushID(i++);
                if (i == 1) ImGui.TextColored(Gold, "> " + StepWindow.Icon(o.Kind) + " " + o.Title);
                else ImGui.Text("  " + StepWindow.Icon(o.Kind) + " " + o.Title);
                if (o.Detail.Length > 0) { ImGui.SameLine(); ImGui.TextDisabled(o.Detail); }
                ImGui.SameLine();
                if (o.Where.IsValid && ImGui.SmallButton("map")) plugin.OpenMapAt(o.Where);
                ImGui.SameLine();
                if (ImGui.SmallButton("skip")) plugin.Planner.Skip(o);
                ImGui.PopID();
            }
            if (Cfg.Skipped.Count > 0 && ImGui.SmallButton($"Un-skip {Cfg.Skipped.Count} objectives")) { Cfg.Skipped.Clear(); plugin.SaveConfig(); plugin.Planner.ForceReplan(); }
        }
        ImGui.EndChild();
    }

    // ---------------- Overview ----------------
    private void DrawOverview()
    {
        if (ImGui.Button("Refresh")) Snapshot();
        ImGui.SameLine();
        ImGui.TextDisabled($"Game data: {Db.Status}");
        ImGui.TextDisabled($"Quest paths: {plugin.QuestPaths.Status}  |  learned hunt spots: {plugin.Learned.Count}");
        ImGui.Separator();

        var quests = Db.Quests.Values.Where(q => q.Kind is not (QuestKind.Repeatable or QuestKind.Seasonal)).ToList();
        Bar("All quests", quests.Count(q => QDone(q.RowId)), quests.Count);
        Bar("Main scenario", Db.MainScenario.Count(q => QDone(q.RowId)), Db.MainScenario.Count);
        var ach = achDone.Values.ToList();
        if (ach.Any(v => v == null)) ImGui.TextDisabled("Achievements: waiting for the server to send your achievement list (open the Achievements window once if this persists).");
        else Bar("Achievements", ach.Count(v => v == true), ach.Count);
        var currents = Db.Zones.Values.SelectMany(z => z.AetherCurrents).ToList();
        Bar("Aether currents", currents.Count(c => Progress.AetherCurrent(c.Id)), currents.Count);
        var vistas = Db.Zones.Values.SelectMany(z => z.Vistas).ToList();
        Bar("Sightseeing log", vistas.Count(v => Progress.Vista(v.Index)), vistas.Count);
        var aes = Db.Zones.Values.SelectMany(z => z.Aetherytes).ToList();
        Bar("Aetherytes", aes.Count(a => Progress.Aetheryte(a.Id)), aes.Count);
        var duties = Db.Duties.Where(d => d.InstanceContentId != 0).ToList();
        Bar("Duties cleared", duties.Count(d => Progress.DutyDone(d.InstanceContentId)), duties.Count);

        ImGui.Separator();
        ImGui.Text("Quests by expansion");
        foreach (var g in quests.GroupBy(q => q.Expansion).OrderBy(g => g.Key))
            Bar(Db.Expansion(g.Key), g.Count(q => QDone(q.RowId)), g.Count());
    }

    // ---------------- Zones ----------------
    private void DrawZones()
    {
        ImGui.InputTextWithHint("##zs", "search zones", ref search, 64);
        if (!ImGui.BeginChild("##zones", new Vector2(-1, -1), false)) { ImGui.EndChild(); return; }
        foreach (var exp in Db.Zones.Values.Where(z => z.Quests.Count + z.Vistas.Count + z.AetherCurrents.Count + z.Aetherytes.Count > 0)
                                           .GroupBy(z => z.Expansion).OrderBy(g => g.Key))
        {
            if (!ImGui.CollapsingHeader(Db.Expansion(exp.Key) + "##ze" + exp.Key)) continue;
            foreach (var z in exp.OrderBy(z => z.Region).ThenBy(z => z.Name))
            {
                if (search.Length > 0 && !z.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                var qDone = z.Quests.Count(QDone);
                var cDone = z.AetherCurrents.Count(c => Progress.AetherCurrent(c.Id));
                var vDone = z.Vistas.Count(v => Progress.Vista(v.Index));
                var aDone = z.Aetherytes.Count(a => Progress.Aetheryte(a.Id));
                ImGui.PushID((int)z.Territory);
                var open = ImGui.TreeNode($"{z.Name}  ({z.Region})  -  quests {qDone}/{z.Quests.Count}  currents {cDone}/{z.AetherCurrents.Count}  vistas {vDone}/{z.Vistas.Count}  aetherytes {aDone}/{z.Aetherytes.Count}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Guide me through this zone")) Guide(GuideMode.Zone, () => Cfg.FocusZone = z.Territory);
                if (open)
                {
                    if (z.HuntSpawnPoints.Count > 0 && ImGui.SmallButton($"S/A/B spawn sweep ({z.HuntSpawnPoints.Count} points)")) Guide(GuideMode.EliteSweep, () => Cfg.FocusZone = z.Territory);
                    foreach (var m in z.EliteMarks) ImGui.TextDisabled($"   Elite mark {"?BAS"[Math.Clamp(m.Rank, 0, 3)]}: {m.Name}");
                    foreach (var id in z.Quests.OrderBy(id => Db.Quests[id].Level))
                        QuestRow(Db.Quests[id]);
                    foreach (var v in z.Vistas) ImGui.TextColored(Progress.Vista(v.Index) ? Green : new Vector4(1, 1, 1, 1), $"   [V] {v.Name}");
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    // ---------------- Quests ----------------
    private static readonly string[] KindNames = Enum.GetNames<QuestKind>();

    private void DrawQuests()
    {
        ImGui.SetNextItemWidth(240);
        ImGui.InputTextWithHint("##qs", "search quests", ref search, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        var kinds = new[] { "All types" }.Concat(KindNames).ToArray();
        var k = questKindFilter + 1;
        if (ImGui.Combo("##qk", ref k, kinds, kinds.Length)) questKindFilter = k - 1;
        ImGui.SameLine();
        ImGui.Checkbox("Hide completed", ref hideDone);

        if (!ImGui.BeginChild("##quests", new Vector2(-1, -1), false)) { ImGui.EndChild(); return; }
        foreach (var exp in Db.Quests.Values.GroupBy(q => q.Expansion).OrderBy(g => g.Key))
        {
            var all = exp.ToList();
            if (!ImGui.CollapsingHeader($"{Db.Expansion(exp.Key)}  -  {all.Count(q => QDone(q.RowId))}/{all.Count}##qe{exp.Key}")) continue;
            foreach (var genre in all.Where(Filter).GroupBy(q => q.Genre.Length > 0 ? q.Genre : q.Kind.ToString()).OrderBy(g => g.Key))
            {
                var list = genre.ToList();
                if (!ImGui.TreeNode($"{genre.Key}  ({list.Count(q => QDone(q.RowId))}/{list.Count})##qg{exp.Key}{genre.Key}")) continue;
                var ordered = list.First().Kind == QuestKind.MainScenario
                    ? list.OrderBy(q => Db.MainScenario.IndexOf(q)).ToList()
                    : list.OrderBy(q => q.Level).ThenBy(q => q.Name).ToList();
                foreach (var q in ordered.Take(1500)) QuestRow(q);
                ImGui.TreePop();
            }
        }
        ImGui.EndChild();
    }

    private bool Filter(QuestInfo q)
    {
        if (hideDone && QDone(q.RowId)) return false;
        if (questKindFilter >= 0 && (int)q.Kind != questKindFilter) return false;
        return search.Length == 0 || q.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void QuestRow(QuestInfo q)
    {
        ImGui.PushID((int)q.RowId);
        var done = QDone(q.RowId);
        var col = done ? Green : Progress.QuestAccepted(q.RowId) ? Gold : new Vector4(1, 1, 1, 1);
        ImGui.TextColored(col, $"   {(done ? "[x]" : "[ ]")} Lv{q.Level} {q.Name}");
        ImGui.SameLine();
        ImGui.TextDisabled($"{q.Kind}{(q.HasDetailedPath ? "" : " (area only)")}{(q.StartNpc.Length > 0 ? " - " + q.StartNpc + ", " + Db.ZoneName(q.Start.Territory) : "")}");
        if (!done)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Guide me")) Guide(GuideMode.Quest, () => Cfg.FocusQuest = q.RowId);
        }
        ImGui.PopID();
    }

    // ---------------- Achievements ----------------
    private void DrawAchievements()
    {
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##as", "search achievements", ref search, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Hide completed", ref hideDone);
        if (achDone.Values.Any(v => v == null))
            ImGui.TextColored(Gold, "Completion unknown until the server sends it - open the in-game Achievements window once.");

        if (!ImGui.BeginChild("##ach", new Vector2(-1, -1), false)) { ImGui.EndChild(); return; }
        foreach (var kind in Db.Achievements.GroupBy(a => a.Kind).OrderBy(g => g.Key))
        {
            var kl = kind.ToList();
            if (!ImGui.CollapsingHeader($"{(kind.Key.Length > 0 ? kind.Key : "Other")}  -  {kl.Count(a => achDone.GetValueOrDefault(a.Id) == true)}/{kl.Count}##ak{kind.Key}")) continue;
            foreach (var cat in kl.GroupBy(a => a.Category).OrderBy(g => g.Key))
            {
                var cl = cat.ToList();
                if (!ImGui.TreeNode($"{cat.Key}  ({cl.Count(a => achDone.GetValueOrDefault(a.Id) == true)}/{cl.Count})##ac{kind.Key}{cat.Key}")) continue;
                foreach (var a in cl)
                {
                    var st = achDone.GetValueOrDefault(a.Id);
                    if (hideDone && st == true) continue;
                    if (search.Length > 0 && !a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && !a.Description.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                    ImGui.PushID((int)a.Id);
                    ImGui.TextColored(st == true ? Green : new Vector4(1, 1, 1, 1), $"   {(st == true ? "[x]" : "[ ]")} {a.Name} ({a.Points}pt)");
                    ImGui.SameLine();
                    ImGui.TextDisabled(a.Description);
                    if (st != true)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton(a.LinkedQuests.Length > 0 ? "Guide me" : "Track")) Guide(GuideMode.Achievement, () => Cfg.FocusAchievement = a.Id);
                    }
                    ImGui.PopID();
                }
                ImGui.TreePop();
            }
        }
        ImGui.EndChild();
    }

    // ---------------- Hunts ----------------
    private void DrawHunts()
    {
        ImGui.TextWrapped("Hunt bills (Mark Bills, Clan Marks, Elite Marks). Targets on a bill you hold show kills; 'Guide me' leads you to the target's area, and to the exact spot once you have seen that monster once.");
        if (ImGui.Button("Guide me through all held bills")) Guide(GuideMode.Hunt, () => Cfg.FocusHuntMark = 0);
        if (!ImGui.BeginChild("##hunt", new Vector2(-1, -1), false)) { ImGui.EndChild(); return; }
        foreach (var bill in Db.Hunts.GroupBy(h => h.MarkIndex).OrderBy(g => g.Key))
        {
            var held = Progress.HuntOrderHeld(bill.Key);
            var first = bill.First();
            if (!ImGui.CollapsingHeader($"{first.BillName}{(held > 0 ? $"  - holding order {held}" : "")}##hb{bill.Key}")) continue;
            ImGui.PushID(bill.Key);
            if (ImGui.SmallButton("Guide me through this bill")) Guide(GuideMode.Hunt, () => Cfg.FocusHuntMark = bill.Key);
            foreach (var h in bill)
            {
                var active = held == h.OrderRow;
                var kills = active ? Progress.HuntKills(h.MarkIndex, h.MobIndex) : 0;
                var col = active ? (kills >= h.NeededKills ? Green : Gold) : new Vector4(0.7f, 0.7f, 0.7f, 1);
                ImGui.TextColored(col, $"   {h.Name} x{h.NeededKills}  -  {h.Zone}{(active ? $"  ({kills}/{h.NeededKills})" : "")}{(plugin.Learned.Get(h.NameId) != null ? "  [spot known]" : "")}");
            }
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    // ---------------- Duties ----------------
    private void DrawDuties()
    {
        ImGui.InputTextWithHint("##ds", "search duties", ref search, 64);
        if (!ImGui.BeginChild("##duties", new Vector2(-1, -1), false)) { ImGui.EndChild(); return; }
        foreach (var exp in Db.Duties.GroupBy(d => d.Expansion).OrderBy(g => g.Key))
        {
            var l = exp.ToList();
            if (!ImGui.CollapsingHeader($"{Db.Expansion(exp.Key)}  -  {l.Count(d => Progress.DutyDone(d.InstanceContentId))}/{l.Count(d => d.InstanceContentId != 0)} cleared##de{exp.Key}")) continue;
            foreach (var type in l.GroupBy(d => d.ContentType).OrderBy(g => g.Key))
            {
                if (!ImGui.TreeNode($"{type.Key}##dt{exp.Key}{type.Key}")) continue;
                foreach (var d in type)
                {
                    if (search.Length > 0 && !d.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                    ImGui.PushID((int)d.CfcId);
                    var done = Progress.DutyDone(d.InstanceContentId);
                    ImGui.TextColored(done ? Green : new Vector4(1, 1, 1, 1), $"   {(done ? "[x]" : "[ ]")} Lv{d.Level} {d.Name}{(d.ItemLevel > 0 ? $" (i{d.ItemLevel})" : "")}");
                    if (d.UnlockQuest != 0 && !QDone(d.UnlockQuest) && Db.Quests.TryGetValue(d.UnlockQuest, out var uq))
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled($"unlock: {uq.Name}");
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Guide me to unlock")) Guide(GuideMode.Quest, () => Cfg.FocusQuest = uq.RowId);
                    }
                    ImGui.PopID();
                }
                ImGui.TreePop();
            }
        }
        ImGui.EndChild();
    }

    // ---------------- Gear ----------------
    private static readonly string[] Slots = ["All", "Main hand", "Off hand", "Head", "Body", "Hands", "Waist", "Legs", "Feet", "Ears", "Neck", "Wrists", "Ring", "Soul crystal"];

    private void DrawGear()
    {
        if (Db.Gear == null)
        {
            if (!Db.GearBuilding) Task.Run(Db.BuildGear);
            ImGui.TextDisabled("Indexing every piece of equipment and where it comes from...");
            return;
        }
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##gs", "search gear", ref search, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        var si = Array.IndexOf(Slots, gearSlot);
        if (ImGui.Combo("##slot", ref si, Slots, Slots.Length)) gearSlot = Slots[Math.Max(0, si)];
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70); ImGui.InputInt("min i", ref gearMinIlvl, 0);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70); ImGui.InputInt("max i", ref gearMaxIlvl, 0);
        ImGui.SameLine();
        ImGui.Checkbox("My job", ref gearMyJob);

        var job = Progress.Job;
        var rows = Db.Gear.Where(g =>
            (gearSlot == "All" || g.Slot == gearSlot) && g.ItemLevel >= gearMinIlvl && g.ItemLevel <= gearMaxIlvl &&
            (!gearMyJob || job == 0 || Progress.JobInCategory(g.JobCategory, job)) &&
            (search.Length == 0 || g.Name.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        ImGui.TextDisabled($"{rows.Count} of {Db.Gear.Count} pieces of equipment");

        if (!ImGui.BeginTable("##gear", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp, new Vector2(-1, -1))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("iLvl/Lv");
        ImGui.TableSetupColumn("Slot/Jobs");
        ImGui.TableSetupColumn("How to get it");
        ImGui.TableSetupColumn("");
        ImGui.TableHeadersRow();
        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(rows.Count);
        while (clipper.Step())
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var g = rows[i];
                ImGui.PushID((int)g.Id);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var owned = Progress.ItemOwned(g.Id) > 0;
                ImGui.TextColored(owned ? Green : new Vector4(1, 1, 1, 1), (owned ? "[owned] " : "") + g.Name);
                ImGui.TableNextColumn(); ImGui.Text($"{g.ItemLevel} / {g.EquipLevel}");
                ImGui.TableNextColumn(); ImGui.TextDisabled($"{g.Slot} - {g.Jobs}");
                ImGui.TableNextColumn(); ImGui.TextWrapped(string.Join("; ", g.Sources));
                ImGui.TableNextColumn();
                if (g.FromQuest != 0 && !QDone(g.FromQuest) && ImGui.SmallButton("Quest")) Guide(GuideMode.Quest, () => Cfg.FocusQuest = g.FromQuest);
                if (g.SourcePoints.Count > 0 && ImGui.SmallButton("Vendor")) plugin.OpenMapAt(g.SourcePoints[0]);
                ImGui.PopID();
            }
        clipper.End();
        ImGui.EndTable();
    }

    // ---------------- Custom (hand-written JSON guides) ----------------
    private void DrawCustom()
    {
        var engine = plugin.Engine;
        ImGui.TextWrapped("Hand-written step guides (JSON in the plugin config folder's Guides/). Auto-generated guides above cover the whole game; use these for your own routes.");
        if (engine.Active == null)
        {
            foreach (var g in engine.Guides)
                if (ImGui.Button($"{g.Title} ({g.Steps.Count} steps)##{g.FileName}")) { engine.Start(g); engine.FastForward(); plugin.SaveSession(); }
            if (ImGui.Button("Reload guides")) engine.LoadGuides(plugin.GuidesDir);
            return;
        }
        var s = engine.CurrentStep;
        ImGui.Text($"{engine.Active.Title} - step {engine.StepIndex + 1}/{engine.Active.Steps.Count}");
        if (s != null) ImGui.TextWrapped($"[{s.Kind}] {s.Text}");
        if (ImGui.Button("< Back")) { engine.Prev(); plugin.SaveSession(); }
        ImGui.SameLine();
        if (ImGui.Button("Next >")) { engine.Next(); plugin.SaveSession(); }
        ImGui.SameLine();
        if (ImGui.Button("Stop")) { engine.Stop(); plugin.SaveSession(); }
    }

    // ---------------- Settings ----------------
    private void DrawSettings()
    {
        var c = Cfg;
        var ch = false;
        ch |= Check("Show step window", c.ShowStepWindow, v => { c.ShowStepWindow = v; plugin.SetStepWindow(v); });
        ch |= Check("Auto-flag the map at each new objective", c.AutoFlagMap, v => c.AutoFlagMap = v);
        ch |= Check("Leveling: include side quests", c.SweepSideQuests, v => c.SweepSideQuests = v);
        ch |= Check("Leveling: include allied society quests", c.SweepAlliedSocieties, v => c.SweepAlliedSocieties = v);
        ch |= Check("Zone mode: include quests above my level", c.ZoneIgnoresLevel, v => c.ZoneIgnoresLevel = v);
        ch |= Check("Include aetherytes", c.ShowAetherytes, v => c.ShowAetherytes = v);
        ch |= Check("Include sightseeing vistas", c.ShowVistas, v => c.ShowVistas = v);
        ch |= Check("Include aether currents", c.ShowAetherCurrents, v => c.ShowAetherCurrents = v);
        ImGui.Separator();
        ch |= Check("On-screen overlay", c.OverlayEnabled, v => c.OverlayEnabled = v);
        ch |= Check("Draw the road (path on the ground)", c.DrawRoad, v => c.DrawRoad = v);
        ch |= Check("Draw the waypoint arrow", c.DrawArrow, v => c.DrawArrow = v);
        ch |= Check("Draw the destination beacon", c.DrawBeacon, v => c.DrawBeacon = v);
        var f = c.ArrowSize; if (ImGui.SliderFloat("Arrow size", ref f, 16, 80)) { c.ArrowSize = f; ch = true; }
        f = c.ArrowY; if (ImGui.SliderFloat("Arrow height from top", ref f, 40, 600)) { c.ArrowY = f; ch = true; }
        f = c.RoadWidth; if (ImGui.SliderFloat("Road width", ref f, 1, 10)) { c.RoadWidth = f; ch = true; }
        f = c.RoadDrawDistance; if (ImGui.SliderFloat("Road draw distance", ref f, 30, 400)) { c.RoadDrawDistance = f; ch = true; }
        var col = c.RoadColor; if (ImGui.ColorEdit4("Road colour", ref col)) { c.RoadColor = col; ch = true; }
        col = c.ArrowColor; if (ImGui.ColorEdit4("Arrow colour", ref col)) { c.ArrowColor = col; ch = true; }
        ImGui.Separator();
        ImGui.TextWrapped("Quest step paths: " + plugin.QuestPaths.Status);
        ImGui.TextDisabled("Source: Questionable (AGPL-3.0) quest path data, downloaded to this machine and read as data only.");
        if (!plugin.QuestPaths.Busy && ImGui.Button("Download / update quest paths")) _ = plugin.RefreshQuestPaths();
        ImGui.TextDisabled("Obstacle-aware roads need the vnavmesh plugin installed and enabled; otherwise the arrow points in a straight line.");
        ImGui.TextDisabled("Elite mark spawn points: HuntHelper (MIT).");
        if (ch) { plugin.SaveConfig(); plugin.Planner.ForceReplan(); }
    }

    private static bool Check(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (!ImGui.Checkbox(label, ref v)) return false;
        set(v);
        return true;
    }
}
