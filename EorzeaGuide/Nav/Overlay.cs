// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using Dalamud.Bindings.ImGui;
using EorzeaGuide.Data;

namespace EorzeaGuide.Nav;

/// Draws the "road" (the navmesh path as a ribbon of chevrons on the ground), a beacon at the
/// destination, and a Zygor-style waypoint arrow at the top of the screen.
public sealed class Overlay
{
    private readonly Plugin plugin;
    public Overlay(Plugin plugin) => this.plugin = plugin;

    public void Draw()
    {
        var cfg = plugin.Config;
        if (!cfg.OverlayEnabled || Plugin.GameGui.GameUiHidden) return;
        var me = Plugin.Objects.LocalPlayer;
        var obj = plugin.Planner.Current;
        if (me == null || obj == null || !obj.Where.IsValid) return;
        var dl = ImGui.GetBackgroundDrawList();

        var sameZone = obj.Where.Territory == Plugin.ClientState.TerritoryType;
        var roadCol = ImGui.GetColorU32(cfg.RoadColor);
        var path = plugin.Nav.Path;

        if (sameZone && cfg.DrawRoad && path.Count > 1)
        {
            // Road: segments plus chevrons every 2.5 yalms, capped to the next ~120 yalms.
            var walked = 0f;
            for (var i = 0; i + 1 < path.Count && walked < cfg.RoadDrawDistance; i++)
            {
                var a = path[i] + new Vector3(0, 0.15f, 0);
                var b = path[i + 1] + new Vector3(0, 0.15f, 0);
                if (Plugin.GameGui.WorldToScreen(a, out var sa) && Plugin.GameGui.WorldToScreen(b, out var sb))
                    dl.AddLine(sa, sb, roadCol, cfg.RoadWidth);
                var seg = Vector3.Distance(a, b);
                for (var t = 2.5f - walked % 2.5f; t < seg; t += 2.5f)
                    Chevron(dl, a + (b - a) * (t / seg), Vector3.Normalize(b - a), roadCol);
                walked += seg;
            }
        }

        if (sameZone && cfg.DrawBeacon)
        {
            var top = obj.Where.Pos + new Vector3(0, 12, 0);
            if (Plugin.GameGui.WorldToScreen(obj.Where.Pos, out var s0) && Plugin.GameGui.WorldToScreen(top, out var s1))
            {
                dl.AddLine(s0, s1, ImGui.GetColorU32(cfg.BeaconColor), 6f);
                dl.AddCircleFilled(s0, 8f, ImGui.GetColorU32(cfg.BeaconColor));
            }
        }

        if (cfg.DrawArrow) DrawArrow(dl, me.Position, obj, sameZone);
    }

    private static void Chevron(ImDrawListPtr dl, Vector3 at, Vector3 dir, uint col)
    {
        var side = Vector3.Normalize(new Vector3(-dir.Z, 0, dir.X)) * 0.6f;
        var tip = at + dir * 0.6f;
        if (Plugin.GameGui.WorldToScreen(tip, out var t) &&
            Plugin.GameGui.WorldToScreen(at - dir * 0.2f + side, out var l) &&
            Plugin.GameGui.WorldToScreen(at - dir * 0.2f - side, out var r))
        {
            dl.AddLine(l, t, col, 3f);
            dl.AddLine(r, t, col, 3f);
        }
    }

    private void DrawArrow(ImDrawListPtr dl, Vector3 player, Planning.Objective obj, bool sameZone)
    {
        var cfg = plugin.Config;
        var vp = ImGui.GetMainViewport();
        var center = vp.Pos + new Vector2(vp.Size.X / 2, cfg.ArrowY);
        var col = ImGui.GetColorU32(cfg.ArrowColor);

        string label;
        if (!sameZone)
        {
            label = $"Travel: {plugin.Db.ZoneName(obj.Where.Territory)}";
            dl.AddText(center - ImGui.CalcTextSize(label) / 2, col, label);
            return;
        }

        // Direction to a point a few yalms down the road, expressed in screen space.
        var ahead = plugin.Nav.Path.Count > 1 ? plugin.Nav.LookAhead(6f) : obj.Where.Pos;
        var flat = new Vector3(ahead.X - player.X, 0, ahead.Z - player.Z);
        var dist = plugin.Nav.Path.Count > 1 ? plugin.Nav.PathLength() : MapMath.Flat(player, obj.Where.Pos);
        if (flat.LengthSquared() < 0.25f) flat = new Vector3(obj.Where.Pos.X - player.X, 0, obj.Where.Pos.Z - player.Z);
        if (flat.LengthSquared() < 0.01f) return;
        flat = Vector3.Normalize(flat);
        if (!Plugin.GameGui.WorldToScreen(player, out var sp) || !Plugin.GameGui.WorldToScreen(player + flat * 3f, out var sq)) return;
        var d2 = sq - sp;
        if (d2.LengthSquared() < 1e-3f) return;
        d2 = Vector2.Normalize(d2);

        var size = cfg.ArrowSize;
        var perp = new Vector2(-d2.Y, d2.X);
        var tip = center + d2 * size;
        var baseL = center - d2 * size * 0.6f + perp * size * 0.6f;
        var baseR = center - d2 * size * 0.6f - perp * size * 0.6f;
        var notch = center - d2 * size * 0.2f;
        var arrived = dist <= Math.Max(obj.Radius, 3f) + 1.5f;
        var c = arrived ? ImGui.GetColorU32(new Vector4(0.3f, 1f, 0.3f, 0.95f)) : col;
        dl.AddTriangleFilled(tip, baseL, notch, c);
        dl.AddTriangleFilled(tip, notch, baseR, c);
        dl.AddTriangle(tip, baseL, baseR, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), 2f);

        label = arrived ? "Arrived" : $"{dist:0} yalms";
        var ts = ImGui.CalcTextSize(label);
        dl.AddText(center + new Vector2(-ts.X / 2, size + 6), col, label);
        var title = obj.Title.Length > 70 ? obj.Title[..70] + "..." : obj.Title;
        var tt = ImGui.CalcTextSize(title);
        dl.AddText(center + new Vector2(-tt.X / 2, size + 8 + ts.Y), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.95f)), title);
    }
}
