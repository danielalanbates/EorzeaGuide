// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;

namespace EorzeaGuide.Nav;

/// Orders a zone's objectives into the shortest open tour starting at the player:
/// nearest-neighbour seed, then 2-opt until no swap helps. Distances are straight-line;
/// the per-leg road drawn on screen is the obstacle-aware navmesh path.
public static class RouteOptimizer
{
    public static List<int> Order(Vector3 start, IReadOnlyList<Vector3> pts)
    {
        var n = pts.Count;
        var order = new List<int>(n);
        if (n == 0) return order;

        var used = new bool[n];
        var cur = start;
        for (var k = 0; k < n; k++)
        {
            var best = -1; var bd = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                if (used[i]) continue;
                var d = Vector3.DistanceSquared(cur, pts[i]);
                if (d < bd) { bd = d; best = i; }
            }
            used[best] = true;
            order.Add(best);
            cur = pts[best];
        }

        if (n < 4) return order;
        Vector3 P(int idx) => idx < 0 ? start : pts[order[idx]];
        var improved = true;
        var guard = 0;
        while (improved && guard++ < 50)
        {
            improved = false;
            for (var i = 0; i < n - 1; i++)
                for (var j = i + 1; j < n; j++)
                {
                    var a = P(i - 1); var b = P(i); var c = P(j);
                    var before = Vector3.Distance(a, b) + (j + 1 < n ? Vector3.Distance(c, P(j + 1)) : 0);
                    var after = Vector3.Distance(a, c) + (j + 1 < n ? Vector3.Distance(b, P(j + 1)) : 0);
                    if (after + 0.01f < before) { order.Reverse(i, j - i + 1); improved = true; }
                }
        }
        return order;
    }
}
