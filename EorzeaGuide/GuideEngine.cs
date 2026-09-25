using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EorzeaGuide;

public enum StepKind
{
    Accept,    // auto-advances when questId is accepted
    Turnin,    // auto-advances when questId is complete
    Travel,    // manual advance; sets a map flag
    Note       // manual advance
}

public sealed class GuideStep
{
    public string Text { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StepKind Kind { get; set; } = StepKind.Note;
    public uint QuestId { get; set; }          // Lumina Quest row id (e.g. 65xxx)
    public uint TerritoryId { get; set; }      // TerritoryType row for map flag
    public uint MapId { get; set; }
    public float X { get; set; }               // "nice" map coords, e.g. 11.7
    public float Y { get; set; }
    public bool HasCoords => TerritoryId != 0 && MapId != 0;
}

public sealed class Guide
{
    public string Title { get; set; } = "Untitled";
    public string Category { get; set; } = "General";
    public List<GuideStep> Steps { get; set; } = new();
    [JsonIgnore] public string FileName { get; set; } = "";
}

public sealed class GuideEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public List<Guide> Guides { get; } = new();
    public Guide? Active { get; private set; }
    public int StepIndex { get; private set; }
    public GuideStep? CurrentStep =>
        Active != null && StepIndex >= 0 && StepIndex < Active.Steps.Count
            ? Active.Steps[StepIndex] : null;

    public string? LoadError { get; private set; }

    public void LoadGuides(string dir)
    {
        Guides.Clear();
        LoadError = null;
        if (!Directory.Exists(dir)) { LoadError = $"No guide dir: {dir}"; return; }
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f))
        {
            try
            {
                var g = JsonSerializer.Deserialize<Guide>(File.ReadAllText(file), JsonOpts);
                if (g == null || g.Steps.Count == 0) continue;
                g.FileName = Path.GetFileName(file);
                Guides.Add(g);
            }
            catch (Exception ex)
            {
                LoadError = $"{Path.GetFileName(file)}: {ex.Message}";
            }
        }
    }

    public void Start(Guide g, int stepIndex = 0)
    {
        Active = g;
        StepIndex = Math.Clamp(stepIndex, 0, g.Steps.Count - 1);
    }

    public void Stop() { Active = null; StepIndex = 0; }
    public void Next() { if (Active != null && StepIndex < Active.Steps.Count - 1) StepIndex++; }
    public void Prev() { if (Active != null && StepIndex > 0) StepIndex--; }

    /// Skip forward past steps the character has already done (completed quests).
    public void FastForward()
    {
        if (Active == null) return;
        while (StepIndex < Active.Steps.Count - 1)
        {
            var s = Active.Steps[StepIndex];
            if (s.QuestId != 0 && IsComplete(s.QuestId)) StepIndex++;
            else break;
        }
    }

    /// Called every ~1s from Framework.Update; returns true if it advanced.
    public bool AutoAdvance()
    {
        var s = CurrentStep;
        if (s == null || s.QuestId == 0) return false;
        var done = s.Kind switch
        {
            StepKind.Accept => IsAccepted(s.QuestId) || IsComplete(s.QuestId),
            StepKind.Turnin => IsComplete(s.QuestId),
            _ => false,
        };
        if (done && Active != null && StepIndex < Active.Steps.Count - 1) { StepIndex++; return true; }
        return false;
    }

    public static bool IsComplete(uint questId) => QuestManager.IsQuestComplete(questId);

    public static unsafe bool IsAccepted(uint questId)
    {
        var qm = QuestManager.Instance();
        return qm != null && qm->IsQuestAccepted(questId);
    }
}
