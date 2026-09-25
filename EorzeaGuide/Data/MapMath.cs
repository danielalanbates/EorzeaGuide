// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EorzeaGuide.Data;

/// Conversions between world coordinates and the "nice" 1..42 map coordinates the game shows.
public static class MapMath
{
    // map = 0.02 * offset + 2048 / sizeFactor + 0.02 * world + 1   (same formula Dalamud's MapUtil uses)
    public static float WorldToMapCoord(float world, ushort sizeFactor, short offset) =>
        0.02f * offset + 2048f / sizeFactor + 0.02f * world + 1f;

    public static float MapCoordToWorldAxis(float coord, ushort sizeFactor, short offset) =>
        (coord - 1f - 2048f / sizeFactor) / 0.02f - offset;

    public static Vector2 WorldToMap(uint mapId, Vector3 world, IDataManager data)
    {
        var m = data.GetExcelSheet<Map>().GetRowOrDefault(mapId);
        if (m == null) return Vector2.Zero;
        return new Vector2(WorldToMapCoord(world.X, m.Value.SizeFactor, m.Value.OffsetX),
                           WorldToMapCoord(world.Z, m.Value.SizeFactor, m.Value.OffsetY));
    }

    /// Height is unknown from map coordinates; callers snap Y to the floor via vnavmesh when possible.
    public static Vector3 MapCoordToWorld(uint mapId, float x, float y, IDataManager data)
    {
        var m = data.GetExcelSheet<Map>().GetRowOrDefault(mapId);
        if (m == null) return Vector3.Zero;
        return new Vector3(MapCoordToWorldAxis(x, m.Value.SizeFactor, m.Value.OffsetX), 0,
                           MapCoordToWorldAxis(y, m.Value.SizeFactor, m.Value.OffsetY));
    }

    public static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
