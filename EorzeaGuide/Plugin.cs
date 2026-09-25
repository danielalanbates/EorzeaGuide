// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EorzeaGuide.Data;
using EorzeaGuide.Planning;
using EorzeaGuide.Nav;
using EorzeaGuide.Windows;

namespace EorzeaGuide;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Config { get; }
    public GameDb Db { get; }
    public QuestPathStore QuestPaths { get; }
    public LearnedPositions Learned { get; }
    public Navigator Nav { get; }
    public Planner Planner { get; }
    public GuideEngine Engine { get; } = new();
    public string GuidesDir { get; }

    private readonly WindowSystem windowSystem = new("EorzeaGuide");
    private readonly MainWindow mainWindow;
    private readonly StepWindow stepWindow;
    private readonly Overlay overlay;
    private string lastFlagKey = "";
    private DateTime lastLegacyTick = DateTime.MinValue;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Inject(this);
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var cfgDir = PluginInterface.GetPluginConfigDirectory();
        Directory.CreateDirectory(cfgDir);

        QuestPaths = new QuestPathStore(cfgDir);
        Learned = new LearnedPositions(cfgDir);
        Db = new GameDb(DataManager);
        Nav = new Navigator();
        Planner = new Planner(this);
        overlay = new Overlay(this);

        GuidesDir = Path.Combine(cfgDir, "Guides");
        Directory.CreateDirectory(GuidesDir);
        CopyBundledGuides();
        Engine.LoadGuides(GuidesDir);
        if (Config.ActiveGuideFile != null && Engine.Guides.FirstOrDefault(g => g.FileName == Config.ActiveGuideFile) is { } g0)
            Engine.Start(g0, Config.ActiveStepIndex);

        var pluginDir = PluginInterface.AssemblyLocation.DirectoryName!;
        Task.Run(async () =>
        {
            QuestPaths.LoadCached();
            if (QuestPaths.Paths.Count == 0 && Config.AutoDownloadQuestPaths) await QuestPaths.DownloadAsync();
            Db.Build(QuestPaths, pluginDir);
            Learned.Want(Db.Hunts.Select(h => h.NameId));
        });

        mainWindow = new MainWindow(this);
        stepWindow = new StepWindow(this) { IsOpen = Config.ShowStepWindow };
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(stepWindow);

        CommandManager.AddHandler("/eguide", new CommandInfo(OnCommand)
        {
            HelpMessage = "EorzeaGuide. /eguide = main window, /eguide step = step window, /eguide next = skip current objective, /eguide quests = dump active quest ids.",
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += () => mainWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenConfigUi += () => mainWindow.IsOpen = true;
        Framework.Update += OnFrameworkUpdate;
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(Config);
    public void OpenMain() => mainWindow.IsOpen = true;
    public void SetStepWindow(bool open) { stepWindow.IsOpen = open; Config.ShowStepWindow = open; SaveConfig(); }

    public async Task RefreshQuestPaths()
    {
        await QuestPaths.DownloadAsync();
        if (Db.Ready) { Db.ApplyPaths(QuestPaths); Planner.ForceReplan(); }
    }

    private void DrawUi()
    {
        windowSystem.Draw();
        try { if (ClientState.IsLoggedIn) overlay.Draw(); }
        catch (Exception ex) { Log.Error(ex, "overlay draw failed"); }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn || Objects.LocalPlayer is not { } me) return;
        try
        {
            var terr = ClientState.TerritoryType;
            Planner.Tick(me.Position, terr);
            Learned.Scan(terr, ClientState.MapId);

            var cur = Planner.Current;
            if (cur != null && cur.Where.IsValid && cur.Where.Territory == terr) Nav.Update(me.Position, cur.Where.Pos, cur.Fly);
            else Nav.Clear();

            if (cur != null && Config.AutoFlagMap && cur.Key != lastFlagKey && cur.Where.IsValid)
            {
                lastFlagKey = cur.Key;
                FlagMap(cur.Where);
            }
            LegacyTick();
        }
        catch (Exception ex) { Log.Error(ex, "tick failed"); }
    }

    public unsafe void FlagMap(WorldPoint p)
    {
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMap.Instance();
        if (agent == null) return;
        agent->SetFlagMapMarker(p.Territory, p.Map != 0 ? p.Map : Db.MapFor(p.Territory), p.Pos, 60561);
    }

    public void OpenMapAt(WorldPoint p) =>
        GameGui.OpenMapWithMapLink(p.Territory, p.Map != 0 ? p.Map : Db.MapFor(p.Territory), p.Pos);

    /// Teleport is only ever triggered by the player clicking a button.
    public unsafe void Teleport(uint aetheryteId)
    {
        var t = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance();
        if (t != null && aetheryteId != 0) t->Teleport(aetheryteId, 0);
    }

    private void CopyBundledGuides()
    {
        var bundled = Path.Combine(PluginInterface.AssemblyLocation.DirectoryName!, "Guides");
        if (!Directory.Exists(bundled)) return;
        foreach (var f in Directory.EnumerateFiles(bundled, "*.json"))
        {
            var dest = Path.Combine(GuidesDir, Path.GetFileName(f));
            if (!File.Exists(dest)) File.Copy(f, dest);
        }
    }

    private void LegacyTick()
    {
        if (Engine.Active == null || !Config.AutoAdvance || (DateTime.UtcNow - lastLegacyTick).TotalSeconds < 1) return;
        lastLegacyTick = DateTime.UtcNow;
        if (Engine.AutoAdvance()) SaveSession();
    }

    public void SaveSession()
    {
        Config.ActiveGuideFile = Engine.Active?.FileName;
        Config.ActiveStepIndex = Engine.StepIndex;
        SaveConfig();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "quests": DumpActiveQuests(); return;
            case "step": stepWindow.IsOpen = !stepWindow.IsOpen; Config.ShowStepWindow = stepWindow.IsOpen; SaveConfig(); return;
            case "next": if (Planner.Current is { } c) Planner.Skip(c); return;
            default: mainWindow.IsOpen = !mainWindow.IsOpen; return;
        }
    }

    private unsafe void DumpActiveQuests()
    {
        var qm = FFXIVClientStructs.FFXIV.Client.Game.QuestManager.Instance();
        if (qm == null) { ChatGui.Print("[EorzeaGuide] QuestManager unavailable."); return; }
        var count = 0;
        foreach (ref readonly var q in qm->NormalQuests)
        {
            if (q.QuestId == 0) continue;
            var rowId = q.QuestId + 65536u;
            var name = Db.Quests.TryGetValue(rowId, out var info) ? info.Name : "?";
            var mapped = info?.HasDetailedPath == true ? "mapped" : "game-data only";
            ChatGui.Print($"[EorzeaGuide] {rowId}  seq {q.Sequence}  {name}  ({mapped})");
            count++;
        }
        ChatGui.Print($"[EorzeaGuide] {count} active quests.");
    }

    public void Dispose()
    {
        SaveSession();
        Learned.Save();
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler("/eguide");
    }
}
