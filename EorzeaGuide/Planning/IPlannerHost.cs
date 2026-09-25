// EorzeaGuide - Copyright (c) 2026 Daniel Bates / Bates LLC. All rights reserved.
// Licensed under PolyForm Noncommercial 1.0.0 with a 10% revenue rider; see LICENSE.

using EorzeaGuide.Data;

namespace EorzeaGuide.Planning;

/// What the planner needs from its surroundings: the plugin in game, the virtual player offline.
public interface IPlannerHost
{
    Configuration Config { get; }
    GameDb Db { get; }
    WorldPoint? LearnedPosition(uint bnpcNameId);
    void SaveConfig();
    DateTime Now { get; }
    void LogError(Exception ex, string message);
}
