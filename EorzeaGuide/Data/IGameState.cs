// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

namespace EorzeaGuide.Data;

/// Everything the guide reads about the character. LiveGameState reads the real client;
/// the virtual player (Tests/VPlayer) supplies a simulated one so every guide can be
/// played to completion offline.
public interface IGameState
{
    bool QuestDone(uint rowId);
    bool QuestAccepted(uint rowId);
    byte QuestSequence(uint rowId);
    IEnumerable<uint> AcceptedQuests();
    bool AetherCurrent(uint id);
    bool Vista(uint index);
    bool Aetheryte(uint id);
    bool DutyDone(uint instanceContentId);
    bool? Achievement(uint id);
    int HuntKills(byte markIndex, byte mobIndex);
    int HuntOrderHeld(byte markIndex);
    int ItemOwned(uint itemId);
    int Level { get; }
    uint Job { get; }
    uint GrandCompany { get; }
    bool JobInCategory(uint category, uint job);
}
