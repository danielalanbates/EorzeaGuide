// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace EorzeaGuide.Data;

/// Reads the real client's memory. Nothing here writes game state.
public sealed unsafe class LiveGameState : IGameState
{
    public bool QuestDone(uint rowId) => QuestManager.IsQuestComplete(rowId);

    public bool QuestAccepted(uint rowId)
    {
        var qm = QuestManager.Instance();
        return qm != null && qm->IsQuestAccepted(rowId);
    }

    public byte QuestSequence(uint rowId) => QuestManager.GetQuestSequence(rowId);

    public IEnumerable<uint> AcceptedQuests()
    {
        var list = new List<uint>();
        var qm = QuestManager.Instance();
        if (qm == null) return list;
        foreach (ref readonly var w in qm->NormalQuests)
            if (w.QuestId != 0 && !w.IsHidden) list.Add(w.QuestId + 65536u);
        return list;
    }

    public bool AetherCurrent(uint id) { var ps = PlayerState.Instance(); return ps != null && ps->IsAetherCurrentUnlocked(id); }
    public bool Vista(uint index) { var ps = PlayerState.Instance(); return ps != null && ps->IsAdventureComplete(index); }
    public bool Aetheryte(uint id) { var ui = UIState.Instance(); return ui != null && ui->IsAetheryteUnlocked(id); }
    public bool DutyDone(uint instanceContentId) => UIState.IsInstanceContentCompleted(instanceContentId);

    private DateTime achRequested = DateTime.MinValue;

    /// Achievement state is only in memory after the server sends it; ask once, then read.
    public bool? Achievement(uint id)
    {
        var a = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
        if (a == null) return null;
        if (!a->IsLoaded())
        {
            if ((DateTime.UtcNow - achRequested).TotalSeconds > 30)
            {
                achRequested = DateTime.UtcNow;
                a->RequestCompletedAchievements();
            }
            return null;
        }
        return a->IsComplete((int)id);
    }

    public int HuntKills(byte markIndex, byte mobIndex) { var m = MobHunt.Instance(); return m == null ? 0 : m->GetKillCount(markIndex, mobIndex); }
    public int HuntOrderHeld(byte markIndex) { var m = MobHunt.Instance(); return m == null ? -1 : m->GetObtainedHuntOrderRowId(markIndex); }

    public int ItemOwned(uint itemId)
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        return im->GetInventoryItemCount(itemId, false, true, true, 0) + im->GetInventoryItemCount(itemId, true, true, true, 0);
    }

    public int Level => Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.Level : 0;
    public uint Job => Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ClassJob.RowId : 0;
    public uint GrandCompany => Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.GrandCompany.RowId : 0;

    private readonly Dictionary<(uint, uint), bool> catCache = new();
    public bool JobInCategory(uint category, uint job) => JobCategories.Contains(Plugin.DataManager, catCache, category, job);
}
