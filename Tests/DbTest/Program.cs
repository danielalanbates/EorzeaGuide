// Offline check: build the whole GameDb from the real game files, the same code the plugin runs.
using System.IO.Compression;
using System.Reflection;
using Dalamud.Plugin.Services;
using EorzeaGuide.Data;
namespace EorzeaGuide { static class Plugin { public static readonly Log Log = new(); } class Log { public void Error(Exception e, string m) => Console.Error.WriteLine(m + ": " + e); public void Warning(Exception e, string m) => Error(e, m); } }

namespace Dalamud.Plugin.Services {
  /// Test-only stand-in for the two IDataManager members GameDb uses.
  public interface IDataManager {
    Lumina.Excel.ExcelSheet<T> GetExcelSheet<T>() where T : struct, Lumina.Excel.IExcelRow<T>;
    Lumina.Excel.SubrowExcelSheet<T> GetSubrowExcelSheet<T>() where T : struct, Lumina.Excel.IExcelSubrow<T>;
  }
}

/// IDataManager over a plain Lumina GameData.
public sealed class LuminaData(Lumina.GameData game) : IDataManager
{
    public Lumina.Excel.ExcelSheet<T> GetExcelSheet<T>() where T : struct, Lumina.Excel.IExcelRow<T> => game.GetExcelSheet<T>(Lumina.Data.Language.English)!;
    public Lumina.Excel.SubrowExcelSheet<T> GetSubrowExcelSheet<T>() where T : struct, Lumina.Excel.IExcelSubrow<T> => game.GetSubrowExcelSheet<T>(Lumina.Data.Language.English)!;
}

static class Program {
  static int Main(string[] a) {
    var sqpack = a[0]; var tgz = a[1]; var pluginDir = a[2];
    var game = new Lumina.GameData(sqpack, new Lumina.LuminaOptions { PanicOnSheetChecksumMismatch = false, DefaultExcelLanguage = Lumina.Data.Language.English });
    IDataManager dm = new LuminaData(game);

    var paths = new QuestPathStore(Path.GetTempPath());
    using (var gz = new GZipStream(File.OpenRead(tgz), CompressionMode.Decompress))
      typeof(QuestPathStore).GetProperty("Paths")!.SetValue(paths, QuestPathStore.ParseTar(gz));

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var db = new GameDb(dm);
    db.Build(paths, pluginDir);
    Console.WriteLine($"build {sw.ElapsedMilliseconds}ms ready={db.Ready} :: {db.Status}");
    if (!db.Ready) return 1;
    var mapped = db.Quests.Values.Count(q => q.HasDetailedPath);
    var withStart = db.Quests.Values.Count(q => q.Start.IsValid);
    var anyStep = db.Quests.Values.Count(q => q.Steps.Count > 0);
    Console.WriteLine($"quests {db.Quests.Count}: detailed path {mapped}, start location {withStart}, any step location {anyStep}");
    foreach (var k in db.Quests.Values.GroupBy(q => q.Kind)) Console.WriteLine($"  {k.Key}: {k.Count()}");
    Console.WriteLine($"MSQ chain {db.MainScenario.Count}: first {string.Join(" > ", db.MainScenario.Take(4).Select(q => q.Name))}  ...  last {db.MainScenario[^1].Name}");
    Console.WriteLine($"zones {db.Zones.Count}; aetherytes {db.Zones.Values.Sum(z => z.Aetherytes.Count)}; vistas {db.Zones.Values.Sum(z => z.Vistas.Count)}; aether currents {db.Zones.Values.Sum(z => z.AetherCurrents.Count)} (located {db.Zones.Values.Sum(z => z.AetherCurrents.Count(c => c.Where != null || c.Quest != 0))}); elite marks {db.Zones.Values.Sum(z => z.EliteMarks.Count)}; spawn points {db.Zones.Values.Sum(z => z.HuntSpawnPoints.Count)}");
    Console.WriteLine($"achievements {db.Achievements.Count} (quest-linked {db.Achievements.Count(x => x.LinkedQuests.Length > 0)}); hunt targets {db.Hunts.Count}; duties {db.Duties.Count} (with unlock quest {db.Duties.Count(d => d.UnlockQuest != 0)})");
    var mln = db.Zones.Values.First(z => z.Name == "Middle La Noscea");
    var ae = mln.Aetherytes[0];
    var map = MapMath.WorldToMap(ae.Where.Map, ae.Where.Pos, dm);
    Console.WriteLine($"Middle La Noscea aetheryte '{ae.Name}' world {ae.Where.Pos} -> map ({map.X:0.0},{map.Y:0.0})  [in-game: Summerford Farms ~(26,17)]");
    // Independent check: game-data quest-giver position vs Questionable's AcceptQuest step.
    int gOk = 0, gBad = 0;
    foreach (var q in db.Quests.Values.Where(q => q.HasDetailedPath && q.Start.IsValid))
    {
      var acc = q.Steps.FirstOrDefault(s => s.Action == "AcceptQuest" && s.Where.IsValid);
      if (acc == null) continue;
      if (System.Numerics.Vector3.Distance(acc.Where.Pos, q.Start.Pos) < 10) gOk++; else gBad++;
    }
    Console.WriteLine($"quest-giver position (game Level data) vs Questionable accept step: {gOk} within 10y, {gBad} further");
    // Independent check: aetheryte positions vs Questionable's hand-measured table.
    var reference = System.Text.Json.JsonSerializer.Deserialize<Dictionary<uint, float[]>>(File.ReadAllText("aetheryte-reference.json"))!;
    int ok = 0, bad = 0, missing = 0;
    foreach (var (id, r) in reference)
    {
      var hit = db.Zones.Values.SelectMany(z => z.Aetherytes).Where(x => x.Id == id).ToList();
      if (hit.Count == 0) { missing++; continue; }
      var d = System.Numerics.Vector3.Distance(hit[0].Where.Pos, new System.Numerics.Vector3(r[0], r[1], r[2]));
      if (d < 15) ok++; else { bad++; if (bad <= 5) Console.WriteLine($"  aetheryte {id} {hit[0].Name}: ours {hit[0].Where.Pos} vs ref ({r[0]},{r[1]},{r[2]}) = {d:0}y off"); }
    }
    Console.WriteLine($"aetheryte positions vs reference: {ok} within 15y, {bad} off, {missing} not in our zones (city aethernet shards etc.)");
    sw.Restart(); db.BuildGear();
    Console.WriteLine($"gear {db.Gear!.Count} items in {sw.ElapsedMilliseconds}ms; with a known source {db.Gear.Count(g => !g.Sources[0].StartsWith("Duty drop"))}; with vendor location {db.Gear.Count(g => g.SourcePoints.Count > 0)}");
    var g0 = db.Gear.First(g => g.Name == "Weathered Shortsword");
    Console.WriteLine($"  e.g. {g0.Name}: i{g0.ItemLevel} {g0.Slot} [{g0.Jobs}] <- {string.Join("; ", g0.Sources)}");
    return 0;
  }
}
