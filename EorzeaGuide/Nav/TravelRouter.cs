// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using EorzeaGuide.Data;

namespace EorzeaGuide.Nav;

/// Chooses how to reach an objective: walk/ride, or teleport to an unlocked aetheryte and go
/// from there. Costs are seconds. CompletionRoute's hearth-vs-walk decision, FFXIV edition.
public static class TravelRouter
{
    public const float RideSpeed = 9f;          // yalms/s, mounted on the ground
    public const float FlySpeed = 12f;          // yalms/s, once the zone's aether currents are done
    public const float WalkPenalty = 1.35f;     // straight line -> real path (terrain, bridges)
    public const float TeleportSeconds = 18f;   // cast + load screen

    public sealed record Plan(uint Aetheryte, string AetheryteName, float Seconds, float DirectSeconds)
    {
        public bool Teleport => Aetheryte != 0;
    }

    public static float TravelSeconds(Vector3 a, Vector3 b, bool flying) =>
        Vector3.Distance(a, b) * (flying ? 1f : WalkPenalty) / (flying ? FlySpeed : RideSpeed);

    public static Plan Choose(GameDb db, uint playerTerritory, Vector3 player, WorldPoint target)
    {
        var zone = db.Zones.GetValueOrDefault(target.Territory);
        var flying = zone != null && zone.AetherCurrentSet != 0 && zone.AetherCurrents.Count > 0 &&
                     zone.AetherCurrents.All(c => Progress.AetherCurrent(c.Id));
        var direct = playerTerritory == target.Territory ? TravelSeconds(player, target.Pos, flying) : float.MaxValue;

        (uint Id, string Name, float Cost) best = (0, "", float.MaxValue);
        if (zone != null)
            foreach (var a in zone.Aetherytes)
            {
                if (!Progress.Aetheryte(a.Id)) continue;
                var c = TeleportSeconds + TravelSeconds(a.Where.Pos, target.Pos, flying);
                if (c < best.Cost) best = (a.Id, a.Name, c);
            }

        // Only suggest a teleport when it clearly wins (20s margin) or when we're in another zone.
        if (best.Id != 0 && (direct == float.MaxValue || best.Cost + 20f < direct))
            return new Plan(best.Id, best.Name, best.Cost, direct);
        return new Plan(0, "", direct, direct);
    }
}
