// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EorzeaGuide.Planning;

namespace EorzeaGuide.Windows;

/// The small always-on Zygor-style box: current objective, the next few, and controls.
public sealed class StepWindow : Window
{
    private readonly Plugin plugin;

    public StepWindow(Plugin plugin) : base("EorzeaGuide - Step###EorzeaGuideStep", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(380, 190);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose()
    {
        plugin.Config.ShowStepWindow = false;
        plugin.SaveConfig();
    }

    public override void Draw()
    {
        var db = plugin.Db;
        if (!db.Ready) { ImGui.TextDisabled("Building guide data: " + db.Status); return; }
        var pl = plugin.Planner;
        ImGui.TextColored(new Vector4(1f, 0.82f, 0.2f, 1f), pl.Headline);
        var cur = pl.Current;
        if (cur == null) { ImGui.TextWrapped("Nothing left for this mode. Pick another mode in /eguide."); return; }

        ImGui.Separator();
        ImGui.PushTextWrapPos();
        ImGui.Text(Icon(cur.Kind) + " " + cur.Title);
        if (cur.Detail.Length > 0) ImGui.TextDisabled(cur.Detail);
        ImGui.PopTextWrapPos();

        if (cur.Where.IsValid && ImGui.SmallButton("Map")) plugin.OpenMapAt(cur.Where);
        if (cur.TravelAetheryte != 0 && cur.Where.Territory != Plugin.ClientState.TerritoryType)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Teleport")) plugin.Teleport(cur.TravelAetheryte);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Skip")) pl.Skip(cur);
        ImGui.SameLine();
        if (ImGui.SmallButton("Open guide")) plugin.OpenMain();
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.Nav.Status);

        if (pl.Queue.Count > 1)
        {
            ImGui.Separator();
            foreach (var o in pl.Queue.Skip(1).Take(4))
                ImGui.TextDisabled($"  then: {Icon(o.Kind)} {o.Title}");
        }
    }

    public static string Icon(ObjKind k) => k switch
    {
        ObjKind.Quest => "[Q]",
        ObjKind.Aetheryte => "[AE]",
        ObjKind.AetherCurrent => "[AC]",
        ObjKind.Vista => "[V]",
        ObjKind.EliteSpawn => "[S/A/B]",
        ObjKind.HuntMark => "[H]",
        ObjKind.Travel => "[>]",
        _ => "[i]",
    };
}
