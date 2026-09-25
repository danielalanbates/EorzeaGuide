// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EorzeaGuide.Data;

public static class JobCategories
{
    /// ClassJobCategory rows are a column of bools named after job abbreviations.
    public static bool Contains(IDataManager data, Dictionary<(uint, uint), bool> cache, uint category, uint job)
    {
        if (category <= 1) return true;                     // 0 = none, 1 = all classes
        if (cache.TryGetValue((category, job), out var ok)) return ok;
        var cj = data.GetExcelSheet<ClassJob>().GetRowOrDefault(job);
        var cat = data.GetExcelSheet<ClassJobCategory>().GetRowOrDefault(category);
        ok = true;
        if (cj != null && cat != null)
        {
            var prop = typeof(ClassJobCategory).GetProperty(cj.Value.Abbreviation.ExtractText());
            if (prop?.GetValue(cat.Value) is bool b) ok = b;
        }
        return cache[(category, job)] = ok;
    }
}
