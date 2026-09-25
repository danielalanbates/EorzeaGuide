// Offline check: parse the Questionable tarball exactly as the plugin does.
using System.IO.Compression;
using EorzeaGuide.Data;
namespace EorzeaGuide { static class Plugin { public static readonly Log Log = new(); } class Log { public void Error(Exception e, string m) => Console.Error.WriteLine(m + ": " + e); } }
static class Program {
  static int Main(string[] a) {
    using var fs = File.OpenRead(a[0]);
    using var gz = new GZipStream(fs, CompressionMode.Decompress);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var map = QuestPathStore.ParseTar(gz);
    var steps = map.Values.Sum(v => v.Count);
    var withPos = map.Values.Sum(v => v.Count(s => s.HasPos));
    Console.WriteLine($"quests={map.Count} steps={steps} withPos={withPos} ms={sw.ElapsedMilliseconds}");
    var q = map[3292];
    foreach (var s in q) Console.WriteLine($"  seq {s.Seq} {s.Action} terr {s.Territory} ({s.X:0.0},{s.Y:0.0},{s.Z:0.0}) data {s.DataId} ac {s.AetherCurrentId} '{s.Comment}'");
    return map.Count > 4000 && q.Count == 6 ? 0 : 1;
  }
}
