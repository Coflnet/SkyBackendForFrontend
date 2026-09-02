using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.Shared;

public class ConfigContainer
{
    public FlipSettings Settings { get; set; }
    public string Name { get; set; }
    public int Version { get; set; }
    public string ChangeNotes { get; set; }
    public string OwnerId { get; set; }
    public string OwnerMinecraftUuid { get; set; }
    public int Price { get; set; }
    public bool Delisted { get; set; }
    public bool ModeratorDelisted { get; set; }
    public Dictionary<int, SettingsDiffer.SettingsDiff> Diffs { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class CreatedConfigs
{
    public HashSet<string> Configs { get; set; } = new();
}
