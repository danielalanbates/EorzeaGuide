// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using Dalamud.Configuration;
using EorzeaGuide.Planning;

namespace EorzeaGuide;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // Guide
    public GuideMode Mode { get; set; } = GuideMode.Leveling;
    public uint FocusQuest { get; set; }
    public uint FocusZone { get; set; }
    public uint FocusAchievement { get; set; }
    public byte FocusHuntMark { get; set; }
    public HashSet<string> Skipped { get; set; } = new();
    public bool SweepSideQuests { get; set; } = true;
    public bool SweepAlliedSocieties { get; set; } = false;
    public bool ZoneIgnoresLevel { get; set; } = true;
    public bool ShowAetherytes { get; set; } = true;
    public bool ShowVistas { get; set; } = true;
    public bool ShowAetherCurrents { get; set; } = true;
    public bool AutoFlagMap { get; set; } = true;
    public bool ShowStepWindow { get; set; } = true;
    public bool AutoDownloadQuestPaths { get; set; } = true;

    // Overlay
    public bool OverlayEnabled { get; set; } = true;
    public bool DrawRoad { get; set; } = true;
    public bool DrawArrow { get; set; } = true;
    public bool DrawBeacon { get; set; } = true;
    public float RoadWidth { get; set; } = 4f;
    public float RoadDrawDistance { get; set; } = 120f;
    public float ArrowSize { get; set; } = 34f;
    public float ArrowY { get; set; } = 110f;
    public Vector4 RoadColor { get; set; } = new(0.2f, 0.85f, 1f, 0.85f);
    public Vector4 ArrowColor { get; set; } = new(1f, 0.82f, 0.2f, 0.95f);
    public Vector4 BeaconColor { get; set; } = new(1f, 0.82f, 0.2f, 0.7f);

    // Legacy JSON step guides (Custom tab)
    public bool AutoAdvance { get; set; } = true;
    public string? ActiveGuideFile { get; set; }
    public int ActiveStepIndex { get; set; }
}
