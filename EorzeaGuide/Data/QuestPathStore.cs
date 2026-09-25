// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using System.Formats.Tar;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;

namespace EorzeaGuide.Data;

/// Hand-mapped per-step quest positions from the Questionable project (AGPL-3.0,
/// github.com/PunishXIV/Questionable). Nothing from it is bundled: the user downloads
/// it at runtime onto their own machine, and it is read as data only.
public sealed class QuestPathStore
{
    public const string SourceUrl = "https://codeload.github.com/PunishXIV/Questionable/tar.gz/refs/heads/new-main";

    public sealed record Step(byte Seq, string Action, uint Territory, float X, float Y, float Z,
                              bool HasPos, uint DataId, uint AetherCurrentId, bool Fly, float Stop, string Comment,
                              uint ContentFinderConditionId = 0);

    private readonly string indexFile;
    public Dictionary<ushort, List<Step>> Paths { get; private set; } = new();
    public string Status { get; private set; } = "not loaded";
    public bool Busy { get; private set; }

    public QuestPathStore(string configDir) => indexFile = Path.Combine(configDir, "questpaths-index.json");

    public void LoadCached()
    {
        try
        {
            if (!File.Exists(indexFile)) { Status = "not downloaded"; return; }
            Paths = JsonSerializer.Deserialize<Dictionary<ushort, List<Step>>>(File.ReadAllText(indexFile)) ?? new();
            Status = $"{Paths.Count} quests mapped (downloaded {File.GetLastWriteTime(indexFile):yyyy-MM-dd})";
        }
        catch (Exception ex) { Status = "cache unreadable: " + ex.Message; }
    }

    public async Task DownloadAsync()
    {
        if (Busy) return;
        Busy = true;
        Status = "downloading...";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EorzeaGuide/0.2 (personal use)");
            await using var net = await http.GetStreamAsync(SourceUrl);
            await using var gz = new GZipStream(net, CompressionMode.Decompress);
            var result = ParseTar(gz);
            Paths = result;
            File.WriteAllText(indexFile, JsonSerializer.Serialize(result));
            Status = $"{result.Count} quests mapped (downloaded {DateTime.Now:yyyy-MM-dd})";
        }
        catch (Exception ex)
        {
            Status = "download failed: " + ex.Message;
            Plugin.Log.Error(ex, "QuestPathStore download failed");
        }
        finally { Busy = false; }
    }

    /// Reads every QuestPaths/**/<id>_<name>.json entry out of the repo tarball.
    public static Dictionary<ushort, List<Step>> ParseTar(Stream tarStream)
    {
        var map = new Dictionary<ushort, List<Step>>();
        using var reader = new TarReader(tarStream);
        TarEntry? e;
        while ((e = reader.GetNextEntry()) != null)
        {
            if (e.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || e.DataStream == null) continue;
            if (!e.Name.Contains("/QuestPaths/") || !e.Name.EndsWith(".json")) continue;
            var file = Path.GetFileName(e.Name);
            var us = file.IndexOf('_');
            if (us <= 0 || !ushort.TryParse(file[..us], out var id)) continue;
            try
            {
                using var ms = new MemoryStream();
                e.DataStream.CopyTo(ms);
                var steps = ParseQuestJson(ms.ToArray());
                if (steps.Count > 0) map[id] = steps;
            }
            catch { /* skip malformed file */ }
        }
        return map;
    }

    public static List<Step> ParseQuestJson(byte[] utf8)
    {
        var list = new List<Step>();
        var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var span = utf8.AsSpan();
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) span = span[3..];
        using var doc = JsonDocument.Parse(span.ToArray(), opts);
        if (!doc.RootElement.TryGetProperty("QuestSequence", out var seqs)) return list;
        foreach (var seq in seqs.EnumerateArray())
        {
            var s = (byte)(seq.TryGetProperty("Sequence", out var sv) ? sv.GetInt32() : 0);
            if (!seq.TryGetProperty("Steps", out var steps)) continue;
            foreach (var st in steps.EnumerateArray())
            {
                float x = 0, y = 0, z = 0; var hasPos = false;
                if (st.TryGetProperty("Position", out var p))
                {
                    x = p.GetProperty("X").GetSingle(); y = p.GetProperty("Y").GetSingle(); z = p.GetProperty("Z").GetSingle();
                    hasPos = true;
                }
                list.Add(new Step(
                    s,
                    Str(st, "InteractionType"),
                    U(st, "TerritoryId"), x, y, z, hasPos,
                    U(st, "DataId"), U(st, "AetherCurrentId"),
                    st.TryGetProperty("Fly", out var f) && f.ValueKind == JsonValueKind.True,
                    st.TryGetProperty("StopDistance", out var sd) && sd.TryGetSingle(out var sdv) ? sdv : 0,
                    Str(st, "Comment"),
                    st.TryGetProperty("DutyOptions", out var duty) ? U(duty, "ContentFinderConditionId") : 0));
            }
        }
        return list;
    }

    private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static uint U(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var u) ? u : 0;

    public static Vector3 Pos(Step s) => new(s.X, s.Y, s.Z);
}
