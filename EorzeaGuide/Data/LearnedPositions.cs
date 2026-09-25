// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Types;

namespace EorzeaGuide.Data;

/// Hunt-bill monsters have no spawn coordinates in the game data, so the first time the
/// character sees one, its position is remembered locally for future bills.
public sealed class LearnedPositions
{
    private readonly string file;
    private Dictionary<uint, float[]> map = new();          // BNpcName -> [territory, map, x, y, z]
    private HashSet<uint> wanted = new();
    private DateTime lastScan = DateTime.MinValue;
    private bool dirty;

    public LearnedPositions(string configDir)
    {
        file = Path.Combine(configDir, "learned-positions.json");
        try { if (File.Exists(file)) map = JsonSerializer.Deserialize<Dictionary<uint, float[]>>(File.ReadAllText(file)) ?? new(); }
        catch { map = new(); }
    }

    public int Count => map.Count;
    public void Want(IEnumerable<uint> nameIds) => wanted = nameIds.ToHashSet();

    public WorldPoint? Get(uint nameId) =>
        map.TryGetValue(nameId, out var v) ? new WorldPoint((uint)v[0], (uint)v[1], new Vector3(v[2], v[3], v[4])) : null;

    public void Scan(uint territory, uint mapId)
    {
        if ((DateTime.UtcNow - lastScan).TotalSeconds < 2 || wanted.Count == 0) return;
        lastScan = DateTime.UtcNow;
        foreach (var o in Plugin.Objects)
        {
            if (o is not IBattleNpc b || !wanted.Contains(b.NameId) || map.ContainsKey(b.NameId)) continue;
            map[b.NameId] = [territory, mapId, b.Position.X, b.Position.Y, b.Position.Z];
            dirty = true;
        }
        if (dirty) Save();
    }

    public void Save()
    {
        try { File.WriteAllText(file, JsonSerializer.Serialize(map)); dirty = false; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "saving learned positions failed"); }
    }
}
