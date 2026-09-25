// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

namespace EorzeaGuide.Data;

/// Read-only view of the character's progress through whichever IGameState is plugged in.
public static class Progress
{
    public static IGameState State { get; set; } = null!;

    public static bool QuestDone(uint rowId) => State.QuestDone(rowId);
    public static bool QuestAccepted(uint rowId) => State.QuestAccepted(rowId);
    public static byte QuestSequence(uint rowId) => State.QuestSequence(rowId);
    public static bool AetherCurrent(uint id) => State.AetherCurrent(id);
    public static bool Vista(uint index) => State.Vista(index);
    public static bool Aetheryte(uint id) => State.Aetheryte(id);
    public static bool DutyDone(uint instanceContentId) => instanceContentId != 0 && State.DutyDone(instanceContentId);
    public static bool? Achievement(uint id) => State.Achievement(id);
    public static int HuntKills(byte markIndex, byte mobIndex) => State.HuntKills(markIndex, mobIndex);
    public static int HuntOrderHeld(byte markIndex) => State.HuntOrderHeld(markIndex);
    public static int ItemOwned(uint itemId) => State.ItemOwned(itemId);
    public static int Level => State.Level;
    public static uint Job => State.Job;
    public static uint GrandCompany => State.GrandCompany;
    public static bool JobInCategory(uint category, uint job) => category <= 1 || State.JobInCategory(category, job);

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
