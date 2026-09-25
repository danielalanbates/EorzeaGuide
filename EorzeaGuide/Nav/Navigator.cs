// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Numerics;
using Dalamud.Plugin.Ipc;
using EorzeaGuide.Data;

namespace EorzeaGuide.Nav;

/// Obstacle-aware paths come from vnavmesh over IPC when it is installed; otherwise a straight
/// line. The guide only DRAWS the path - it never moves the character.
public sealed class Navigator
{
    private readonly ICallGateSubscriber<bool> isReady;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>> pathfind;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;

    public List<Vector3> Path { get; private set; } = new();
    public bool PathIsNavmesh { get; private set; }
    public string Status { get; private set; } = "";

    private Vector3 pathTarget;
    private Vector3 pathFrom;
    private Task<List<Vector3>>? pending;
    private DateTime lastRequest = DateTime.MinValue;

    public Navigator()
    {
        var pi = Plugin.PluginInterface;
        isReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfind = pi.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>("vnavmesh.Nav.Pathfind");
        pointOnFloor = pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
    }

    public bool NavmeshReady
    {
        get
        {
            try { return isReady.InvokeFunc(); }
            catch { return false; }
        }
    }

    public Vector3 SnapToFloor(Vector3 p)
    {
        // A zero height means "unknown" (map-marker sources); search down from high above instead.
        try { return pointOnFloor.InvokeFunc(p with { Y = p.Y == 0 ? 1000f : p.Y + 50 }, false, 5f) ?? p; }
        catch { return p; }
    }

    public void Clear() { Path = new(); pending = null; pathTarget = default; }

    /// Called every frame-tick; keeps a path from the player to target fresh.
    public void Update(Vector3 player, Vector3 target, bool fly)
    {
        if (pending != null)
        {
            if (!pending.IsCompleted) return;
            if (pending.IsCompletedSuccessfully && pending.Result is { Count: > 0 } p)
            {
                Path = new List<Vector3> { pathFrom };
                Path.AddRange(p);
                PathIsNavmesh = true;
                Status = $"navmesh path, {Path.Count} points";
            }
            else Status = "navmesh found no path; showing direct line";
            pending = null;
        }

        var targetMoved = Vector3.Distance(target, pathTarget) > 1f;
        var offPath = Path.Count > 1 && DistanceToPath(player) > 12f;
        if (!targetMoved && !offPath && Path.Count > 0) { TrimPassed(player); return; }
        if ((DateTime.UtcNow - lastRequest).TotalSeconds < 1.5 && !targetMoved) return;

        lastRequest = DateTime.UtcNow;
        pathTarget = target;
        pathFrom = player;
        Path = new List<Vector3> { player, target };
        PathIsNavmesh = false;

        if (!NavmeshReady) { Status = "vnavmesh not installed/ready - direct line"; return; }
        try { pending = pathfind.InvokeFunc(player, target, fly); Status = "pathfinding..."; }
        catch (Exception ex) { Status = "vnavmesh IPC error: " + ex.Message; }
    }

    private float DistanceToPath(Vector3 p)
    {
        var best = float.MaxValue;
        for (var i = 0; i + 1 < Path.Count; i++) best = Math.Min(best, SegDist(p, Path[i], Path[i + 1]));
        return best;
    }

    /// Drop path points already walked past so the arrow always points ahead.
    private void TrimPassed(Vector3 player)
    {
        while (Path.Count > 2 && MapMath.Flat(player, Path[1]) < 4f) Path.RemoveAt(0);
        if (Path.Count > 0) Path[0] = player;
    }

    public static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-4f ? 0 : Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return Vector3.Distance(p, a + ab * t);
    }

    public float PathLength()
    {
        var d = 0f;
        for (var i = 0; i + 1 < Path.Count; i++) d += Vector3.Distance(Path[i], Path[i + 1]);
        return d;
    }

    /// A point ~lookahead yalms along the path; what the guide arrow points at.
    public Vector3 LookAhead(float lookahead)
    {
        if (Path.Count == 0) return default;
        var left = lookahead;
        for (var i = 0; i + 1 < Path.Count; i++)
        {
            var seg = Vector3.Distance(Path[i], Path[i + 1]);
            if (seg >= left) return Vector3.Lerp(Path[i], Path[i + 1], seg < 1e-4f ? 0 : left / seg);
            left -= seg;
        }
        return Path[^1];
    }
}
