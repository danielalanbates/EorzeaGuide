// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;

namespace EorzeaGuide.Data;

/// A place in the world: territory + map + 3D position (world units / yalms).
public readonly record struct WorldPoint(uint Territory, uint Map, Vector3 Pos)
{
    public bool IsValid => Territory != 0;
}

public enum QuestKind { MainScenario, Feature, ClassJob, AlliedSociety, Side, Repeatable, Seasonal, Other }

public sealed class QuestStep
{
    public byte Sequence;
    public string Action = "";      // AcceptQuest, Interact, Combat, WalkTo, CompleteQuest, ...
    public string Text = "";        // human-readable instruction
    public WorldPoint Where;
    public float Radius = 3f;
    public uint DataId;             // ENpc/EObj/BNpc id the step targets
    public uint AetherCurrentId;
    public bool Fly;
    public bool FromQuestionable;   // true = hand-mapped path, false = game-data fallback
}

public sealed class QuestInfo
{
    public uint RowId;
    public ushort ShortId => (ushort)(RowId - 65536);
    public string Name = "";
    public int Level;
    public uint ClassJobCategory;
    public uint Expansion;
    public string Genre = "";
    public string Section = "";
    public QuestKind Kind;
    public bool Repeatable;
    public uint Festival;
    public uint GrandCompany;
    public uint BeastTribe;
    public WorldPoint Start;
    public string StartNpc = "";
    public uint StartNpcId;
    public uint[] Prereqs = [];
    public bool PrereqAny;
    public uint[] Locks = [];
    public uint UnlocksInstanceContent;
    public ushort SortKey;
    public List<QuestStep> Steps = new();
    public bool HasDetailedPath;
    public uint[] RewardItems = [];
}

public sealed class ZoneInfo
{
    public uint Territory;
    public uint Map;
    public string Name = "";
    public string Region = "";
    public uint Expansion;
    public List<uint> Quests = new();                        // quests that START here
    public List<(uint Id, WorldPoint? Where, uint Quest)> AetherCurrents = new();
    public uint AetherCurrentSet;
    public List<(uint Index, string Name, WorldPoint Where)> Vistas = new();
    public List<(uint Id, string Name, WorldPoint Where)> Aetherytes = new();
    public List<(uint NameId, string Name, int Rank)> EliteMarks = new();   // rank: 1=B 2=A 3=S
    public List<WorldPoint> HuntSpawnPoints = new();
}

public sealed class AchievementInfo
{
    public uint Id;
    public string Name = "";
    public string Description = "";
    public string Category = "";
    public string Kind = "";
    public int Points;
    public byte Type;
    public uint[] LinkedQuests = [];
    public uint RewardItem;
    public uint Title;
}

public sealed class HuntTarget
{
    public byte MarkIndex;          // MobHuntOrderType row
    public uint OrderRow;           // MobHuntOrder row
    public byte MobIndex;           // subrow within the order
    public string BillName = "";
    public uint NameId;             // BNpcName
    public string Name = "";
    public int NeededKills;
    public bool Elite;
    public uint Territory;
    public uint Map;
    public string Zone = "";
}

public sealed class DutyInfo
{
    public uint CfcId;
    public string Name = "";
    public string ContentType = "";
    public int Level;
    public int ItemLevel;
    public uint Expansion;
    public uint InstanceContentId;  // 0 when not an InstanceContent duty
    public uint UnlockQuest;
}

public sealed class GearItem
{
    public uint Id;
    public string Name = "";
    public int ItemLevel;
    public int EquipLevel;
    public string Slot = "";
    public string Category = "";
    public string Jobs = "";
    public uint JobCategory;
    public List<string> Sources = new();
    public List<WorldPoint> SourcePoints = new();
    public uint FromQuest;
    public uint FromAchievement;
}
