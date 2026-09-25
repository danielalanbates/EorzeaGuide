// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.
//
// Virtual player: runs the REAL Planner against a simulated character and plays the guide to
// completion - walks to each objective, performs it, lets the planner advance - logging every
// stall. Same idea as CompletionRoute's tools/vplayer.lua.
//
//   dotnet run -c Release -- <sqpack> <questionable.tgz> <pluginDir> <outDir> [job=26] [mode=all]

using System.IO.Compression;
using System.Numerics;
using EorzeaGuide;
using EorzeaGuide.Data;
using EorzeaGuide.Planning;

sealed class SimState(Dalamud.Plugin.Services.IDataManager dm) : IGameState
{
    public HashSet<uint> Done = new(), Aetherytes = new(), Currents = new(), Vistas = new();
    public Dictionary<uint, byte> Accepted = new();
    public int Lvl = 1; public uint JobId = 26; public uint Gc = 1;
    private readonly Dictionary<(uint, uint), bool> cache = new();

    public bool QuestDone(uint id) => Done.Contains(id);
    public bool QuestAccepted(uint id) => Accepted.ContainsKey(id);
    public byte QuestSequence(uint id) => Accepted.GetValueOrDefault(id);
    public IEnumerable<uint> AcceptedQuests() => Accepted.Keys.ToList();
    public bool AetherCurrent(uint id) => Currents.Contains(id);
    public bool Vista(uint i) => Vistas.Contains(i);
    public bool Aetheryte(uint id) => Aetherytes.Contains(id);
    public bool DutyDone(uint id) => false;
    public bool? Achievement(uint id) => false;
    public int HuntKills(byte m, byte i) => 0;
    public int HuntOrderHeld(byte m) => -1;
    public int ItemOwned(uint id) => 0;
    public int Level => Lvl;
    public uint Job => JobId;
    public uint GrandCompany => Gc;
    public bool JobInCategory(uint c, uint j) => JobCategories.Contains(dm, cache, c, j);
}

sealed class Host(GameDb db) : IPlannerHost
{
    public Configuration Config { get; } = new();
    public GameDb Db => db;
    public DateTime Clock = new(2026, 1, 1);
    public DateTime Now => Clock;
    public WorldPoint? LearnedPosition(uint id) => null;
    public void SaveConfig() { }
    public void LogError(Exception ex, string m) => Console.Error.WriteLine($"{m}: {ex}");
}

static class Program
{
    static int Main(string[] a)
    {
        var (sqpack, tgz, pluginDir, outDir) = (a[0], a[1], a[2], a[3]);
        var job = a.Length > 4 ? uint.Parse(a[4]) : 26u;
        var mode = a.Length > 5 ? a[5] : "all";
        Directory.CreateDirectory(outDir);

        var game = new Lumina.GameData(sqpack, new Lumina.LuminaOptions { PanicOnSheetChecksumMismatch = false });
        var dm = new LuminaData(game);
        var paths = new QuestPathStore(Path.GetTempPath());
        using (var gz = new GZipStream(File.OpenRead(tgz), CompressionMode.Decompress))
            typeof(QuestPathStore).GetProperty("Paths")!.SetValue(paths, QuestPathStore.ParseTar(gz));
        var db = new GameDb(dm);
        db.Build(paths, pluginDir);
        Console.WriteLine("GameDb: " + db.Status);
        if (!db.Ready) return 2;

        var fails = 0;
        if (mode is "all" or "leveling") fails += RunLeveling(db, dm, job, outDir);
        if (mode is "all" or "zones") fails += RunZones(db, dm, outDir);
        return fails == 0 ? 0 : 1;
    }

    // ---------------- simulation core ----------------

    sealed class Run
    {
        public SimState S = null!;
        public Host H = null!;
        public Planner P = null!;
        public uint Terr; public Vector3 Pos;
        public int Actions, Teleports, UnmappedSteps, Stalls, LevelJumps;
        public double Walked;
        public List<string> Log = new();
    }

    static Run NewRun(GameDb db, Dalamud.Plugin.Services.IDataManager dm, uint job)
    {
        var s = new SimState(dm) { JobId = job };
        Progress.State = s;
        var h = new Host(db);
        return new Run { S = s, H = h, P = new Planner(h) };
    }

    /// One planner step: tick, act on the current objective. Returns false when nothing is left.
    static bool Step(Run r, GameDb db, out Objective? cur)
    {
        r.H.Clock = r.H.Clock.AddSeconds(3);
        r.P.ForceReplan();
        r.P.Tick(r.Pos, r.Terr);
        cur = r.P.Current;
        if (cur == null || cur.Kind == ObjKind.Info) return false;
        r.Actions++;

        if (cur.Where.IsValid)
        {
            if (cur.Where.Territory != r.Terr) r.Teleports++;
            else r.Walked += Vector3.Distance(r.Pos, cur.Where.Pos);
            r.Terr = cur.Where.Territory; r.Pos = cur.Where.Pos;
            // Stand there long enough for the planner's 2s dwell rule.
            for (var i = 0; i < 3; i++) { r.H.Clock = r.H.Clock.AddSeconds(1.5); r.P.Tick(r.Pos, r.Terr); }
        }

        switch (cur.Kind)
        {
            case ObjKind.Quest: DoQuest(r, db, cur); break;
            case ObjKind.Aetheryte: r.S.Aetherytes.Add(cur.Id); break;
            case ObjKind.Vista: r.S.Vistas.Add(cur.Id); break;
            case ObjKind.AetherCurrent: r.S.Currents.Add(cur.Id); break;
            default: r.P.Skip(cur); break;
        }
        return true;
    }

    /// Emulates the server: accepting sets the first real sequence; finishing the last mapped
    /// step of a sequence moves to the next one; sequence 255 hands the quest in.
    static void DoQuest(Run r, GameDb db, Objective cur)
    {
        var q = db.Quests[cur.QuestId];
        var s = r.S;
        if (!s.Accepted.ContainsKey(q.RowId))
        {
            if (q.Level > s.Lvl) { s.Lvl = q.Level; r.LevelJumps++; }
            s.Accepted[q.RowId] = NextSeq(q, 0);
            return;
        }
        var seq = s.Accepted[q.RowId];
        if (!cur.Where.IsValid) r.UnmappedSteps++;
        var here = q.Steps.Where(x => x.Sequence == seq && x.Where.IsValid).ToList();
        // Only the last mapped step of a sequence changes the server-side sequence.
        var atLast = here.Count <= 1 || !cur.Where.IsValid ||
                     (int.TryParse(cur.Key[(cur.Key.LastIndexOf('-') + 1)..], out var k) && k >= here.Count - 1);
        if (!atLast) return;
        if (seq == 255) { s.Accepted.Remove(q.RowId); s.Done.Add(q.RowId); return; }
        s.Accepted[q.RowId] = NextSeq(q, seq);
    }

    static byte NextSeq(QuestInfo q, byte after)
    {
        var next = q.Steps.Select(x => x.Sequence).Where(x => x > after && x != 255).DefaultIfEmpty((byte)255).Min();
        return next;
    }

    /// Detects the planner offering the same objective forever.
    static bool Stuck(Run r, Dictionary<string, int> seen, Objective cur, int limit = 6)
    {
        seen[cur.Key] = seen.GetValueOrDefault(cur.Key) + 1;
        if (seen[cur.Key] <= limit) return false;
        r.Stalls++;
        var q = cur.QuestId != 0 && r.H.Db.Quests.TryGetValue(cur.QuestId, out var qi) ? qi : null;
        r.Log.Add($"STALL\t{cur.Kind}\t{cur.QuestId}\t{q?.Name}\t{cur.Title}\t{cur.Where.Territory}\tseq={(q != null ? r.S.QuestSequence(q.RowId) : 0)}");
        if (q != null) { r.S.Accepted.Remove(q.RowId); r.S.Done.Add(q.RowId); }   // force past it so the run continues
        else r.P.Skip(cur);
        seen.Remove(cur.Key);
        return true;
    }

    // ---------------- leveling: the whole MSQ with zone sweeps ----------------

    static int RunLeveling(GameDb db, Dalamud.Plugin.Services.IDataManager dm, uint job, string outDir)
    {
        var r = NewRun(db, dm, job);
        r.H.Config.Mode = GuideMode.Leveling;
        // Start where the first available MSQ quest is given.
        var first = db.MainScenario.First(q => Progress.Available(q, true) && q.Start.IsValid);
        r.Terr = first.Start.Territory; r.Pos = first.Start.Pos;
        var seen = new Dictionary<string, int>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int maxActions = 200_000;
        while (r.Actions < maxActions && Step(r, db, out var cur))
            if (cur != null) Stuck(r, seen, cur);

        var msqDone = db.MainScenario.Count(q => r.S.Done.Contains(q.RowId));
        var reachable = db.MainScenario.Count(q => JobCategories.Contains(dm, new(), q.ClassJobCategory, job) && (q.GrandCompany == 0 || q.GrandCompany == r.S.Gc));
        var line = $"LEVELING job={job}: MSQ {msqDone}/{reachable} for this job ({db.MainScenario.Count} incl. other start cities/GCs); " +
                   $"quests done {r.S.Done.Count}; actions {r.Actions}; teleports {r.Teleports}; walked {r.Walked / 1000:0.0}k yalms; " +
                   $"aetherytes {r.S.Aetherytes.Count}; vistas {r.S.Vistas.Count}; currents {r.S.Currents.Count}; " +
                   $"steps with no location {r.UnmappedSteps}; stalls {r.Stalls}; level jumps {r.LevelJumps}; {sw.ElapsedMilliseconds}ms";
        Console.WriteLine(line);
        File.WriteAllLines(Path.Combine(outDir, $"vplayer_leveling_job{job}.tsv"), r.Log.Prepend(line));
        var missed = db.MainScenario.Where(q => !r.S.Done.Contains(q.RowId) && JobCategories.Contains(dm, new(), q.ClassJobCategory, job) && (q.GrandCompany == 0 || q.GrandCompany == r.S.Gc))
                                    .Select(q => $"MISSED\t{q.RowId}\t{q.Name}\tlv{q.Level}\tstart={q.Start.Territory}\tprereqs={string.Join(",", q.Prereqs)}").ToList();
        File.AppendAllLines(Path.Combine(outDir, $"vplayer_leveling_job{job}.tsv"), missed);
        Console.WriteLine($"  MSQ quests never reached: {missed.Count} (first: {missed.FirstOrDefault()})");
        return r.Stalls > 0 || missed.Count > 0 ? 1 : 0;
    }

    // ---------------- zones: complete every zone from a max-level, post-MSQ character ----------------

    static int RunZones(GameDb db, Dalamud.Plugin.Services.IDataManager dm, string outDir)
    {
        var rows = new List<string> { "zone\tterritory\tactions\tquests_done\tleft_after\tstalls\tunmapped" };
        int totalLeft = 0, totalStalls = 0, zones = 0;
        var baseline = NewRun(db, dm, 26);
        foreach (var q in db.MainScenario) baseline.S.Done.Add(q.RowId);   // post-MSQ character
        baseline.S.Lvl = 100;
        foreach (var z in db.Zones.Values.Where(z => z.Quests.Count + z.Vistas.Count + z.AetherCurrents.Count + z.Aetherytes.Count > 0).OrderBy(z => z.Territory))
        {
            var r = NewRun(db, dm, 26);
            r.S.Done.UnionWith(baseline.S.Done); r.S.Lvl = 100;
            r.H.Config.Mode = GuideMode.Zone; r.H.Config.FocusZone = z.Territory;
            r.Terr = z.Territory; r.Pos = z.Aetherytes.Count > 0 ? z.Aetherytes[0].Where.Pos : Vector3.Zero;
            var seen = new Dictionary<string, int>();
            var before = r.S.Done.Count;
            while (r.Actions < 20_000 && Step(r, db, out var cur))
                if (cur != null) Stuck(r, seen, cur);
            r.H.Clock = r.H.Clock.AddSeconds(3); r.P.ForceReplan(); r.P.Tick(r.Pos, r.Terr);
            var left = r.P.Queue.Count(o => o.Kind != ObjKind.Info);
            rows.Add($"{z.Name}\t{z.Territory}\t{r.Actions}\t{r.S.Done.Count - before}\t{left}\t{r.Stalls}\t{r.UnmappedSteps}");
            rows.AddRange(r.Log.Select(l => "  " + l));
            totalLeft += left; totalStalls += r.Stalls; zones++;
        }
        var line = $"ZONES: {zones} zones played; objectives left over {totalLeft}; stalls {totalStalls}";
        Console.WriteLine(line);
        File.WriteAllLines(Path.Combine(outDir, "vplayer_zones.tsv"), rows.Prepend(line));
        return totalLeft > 0 || totalStalls > 0 ? 1 : 0;
    }
}
