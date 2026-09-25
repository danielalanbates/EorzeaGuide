// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace EorzeaGuide.Data;

/// Read-only views of the character's progress. Nothing here writes game state.
public static unsafe class Progress
{
    public static bool QuestDone(uint rowId) => QuestManager.IsQuestComplete(rowId);

    public static bool QuestAccepted(uint rowId)
    {
        var qm = QuestManager.Instance();
        return qm != null && qm->IsQuestAccepted(rowId);
    }

    public static byte QuestSequence(uint rowId) => QuestManager.GetQuestSequence(rowId);

    public static bool AetherCurrent(uint id)
    {
        var ps = PlayerState.Instance();
        return ps != null && ps->IsAetherCurrentUnlocked(id);
    }

    public static bool Vista(uint index)
    {
        var ps = PlayerState.Instance();
        return ps != null && ps->IsAdventureComplete(index);
    }

    public static bool Aetheryte(uint id)
    {
        var ui = UIState.Instance();
        return ui != null && ui->IsAetheryteUnlocked(id);
    }

    public static bool DutyDone(uint instanceContentId) => instanceContentId != 0 && UIState.IsInstanceContentCompleted(instanceContentId);

    private static DateTime achRequested = DateTime.MinValue;

    /// Achievement state is only in memory after the server sends it; ask once, then read.
    public static bool? Achievement(uint id)
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

    public static int HuntKills(byte markIndex, byte mobIndex)
    {
        var m = MobHunt.Instance();
        return m == null ? 0 : m->GetKillCount(markIndex, mobIndex);
    }

    public static int HuntOrderHeld(byte markIndex)
    {
        var m = MobHunt.Instance();
        return m == null ? -1 : m->GetObtainedHuntOrderRowId(markIndex);
    }

    public static int ItemOwned(uint itemId)
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;
        return im->GetInventoryItemCount(itemId, false, true, true, 0) + im->GetInventoryItemCount(itemId, true, true, true, 0);
    }

    public static int Level => Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.Level : 0;
    public static uint Job => Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ClassJob.RowId : 0;
    public static uint GrandCompany => Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.GrandCompany.RowId : 0;

    private static readonly Dictionary<(uint Cat, uint Job), bool> catCache = new();

    /// ClassJobCategory rows are a column of bools named after job abbreviations.
    public static bool JobInCategory(uint category, uint job)
    {
        if (category <= 1) return true;                     // 0 = none, 1 = all classes
        if (catCache.TryGetValue((category, job), out var ok)) return ok;
        var cj = Plugin.DataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(job);
        var cat = Plugin.DataManager.GetExcelSheet<ClassJobCategory>().GetRowOrDefault(category);
        ok = true;
        if (cj != null && cat != null)
        {
            var prop = typeof(ClassJobCategory).GetProperty(cj.Value.Abbreviation.ExtractText());
            if (prop?.GetValue(cat.Value) is bool b) ok = b;
        }
        return catCache[(category, job)] = ok;
    }

    /// Can the current character pick this quest up right now?
    public static bool Available(QuestInfo q, bool ignoreLevel = false)
    {
        if (QuestDone(q.RowId) && !q.Repeatable) return false;
        if (q.Prereqs.Length > 0)
        {
            var met = q.PrereqAny ? q.Prereqs.Any(QuestDone) : q.Prereqs.All(QuestDone);
            if (!met) return false;
        }
        if (q.Locks.Any(QuestDone) || q.Locks.Any(QuestAccepted)) return false;
        if (!ignoreLevel && q.Level > Level) return false;
        if (q.GrandCompany != 0 && q.GrandCompany != GrandCompany) return false;
        return JobInCategory(q.ClassJobCategory, Job);
    }
}
