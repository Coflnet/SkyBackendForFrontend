using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;
public class SettingsDiffer
{
    /// <summary>
    /// Compares two settings objects and returns the differences
    /// </summary>
    /// <param name="oldSettings">The old settings</param>
    /// <param name="newSettings">The new settings</param>
    /// <returns>The differences between the two settings</returns>
    public static SettingsDiff GetDifferences(FlipSettings oldSettings, FlipSettings newSettings)
    {
        var differences = new SettingsDiff();
        if (oldSettings == null || newSettings == null)
        {
            return differences;
        }
        // iterate over all properties of settings
        AssignSetCommandsDiff(oldSettings, newSettings, differences);
        AssignSetCommandsDiff(oldSettings.ModSettings, newSettings.ModSettings, differences, "mod");
        AssignSetCommandsDiff(oldSettings.Visibility, newSettings.Visibility, differences, "show");


        CompareList(differences.BlacklistAdded, differences.BlacklistChanged, differences.BlacklistRemoved, oldSettings.BlackList, newSettings.BlackList);
        CompareList(differences.WhitelistAdded, differences.WhitelistChanged, differences.WhitelistRemoved, oldSettings.WhiteList, newSettings.WhiteList);

        return differences;
    }

    /// <summary>
    /// Creates an exportable config containing the user's additions and overrides,
    /// without including unchanged settings from the current Expert Config.
    /// </summary>
    /// <remarks>
    /// The combined config must be based on the supplied current Expert Config.
    /// Deletions are intentionally omitted because a standalone <see cref="FlipSettings"/>
    /// cannot represent removal tombstones.
    /// </remarks>
    public static FlipSettings GetUserDifferenceConfig(FlipSettings combinedUserConfig, FlipSettings currentExpertConfig)
    {
        var difference = GetDifferences(currentExpertConfig, combinedUserConfig);
        var userConfig = new FlipSettings
        {
            BlackList = [],
            WhiteList = []
        };
        return new SettingsDiffer().ApplyDiff(userConfig, difference);
    }

    public FlipSettings ApplyDiff(FlipSettings settings, SettingsDiff settingsDiff, bool skipNonfilter = false)
    {
        var updater = new SettingsUpdater();
        var error = new List<(string message, ListEntry affected)>();
        if (!skipNonfilter)
        {
            foreach (var command in settingsDiff.SetCommands)
            {
                var parts = command.Split(' ');
                updater.Update(settings, parts[0], string.Join(' ', parts.Skip(1)));
            }
        }

        var blackList = settings.BlackList ?? new List<ListEntry>();
        var whiteList = settings.WhiteList ?? new List<ListEntry>();
        UpdateFilterList(error, blackList, settingsDiff.BlacklistAdded, settingsDiff.BlacklistChanged, settingsDiff.BlacklistRemoved);
        UpdateFilterList(error, whiteList, settingsDiff.WhitelistAdded, settingsDiff.WhitelistChanged, settingsDiff.WhitelistRemoved);

        return settings;
    }

    private static void UpdateFilterList(List<(string message, ListEntry affected)> error, List<ListEntry> currentList, List<ListEntry> added, Dictionary<string, ListEntry> changed, List<ListEntry> removed)
    {
        var listLookup = currentList.ToLookup(e => GetKey(e));
        foreach (var command in added)
        {
            if (listLookup.Contains(GetKey(command)) && listLookup[GetKey(command)].Any(e => ListEntry.comparer.Equals(command.filter, e.filter)))
            {
                error.Add(($"Could not add blacklist entry, already exists", command));
                continue;
            }
            currentList.Add(command);
        }
        foreach (var command in changed)
        {
            var previous = currentList.FirstOrDefault(e => GetKey(e) == command.Key);
            if (previous != null)
            {
                currentList.Remove(previous);
            }
            else
            {
                error.Add(($"Could not find previous entry to remove", command.Value));
            }
            currentList.Add(command.Value);
        }
        var alltoRemove = removed.Concat(currentList.GroupBy(e => GetKey(e)).SelectMany(g => g.Skip(1)).ToList());
        foreach (var command in alltoRemove)
        {
            var match = currentList.FirstOrDefault(e => GetKey(e) == GetKey(command));
            if (match != default)
                currentList.Remove(match);
        }
    }

    private static void CompareList(List<ListEntry> added, Dictionary<string, ListEntry> changed, List<ListEntry> removed, List<ListEntry> oldBlacklist, List<ListEntry> newBlacklist)
    {
        if (oldBlacklist == null || newBlacklist == null)
        {
            return;
        }
        var oldLookup = oldBlacklist.ToLookup(e => GetKey(e));
        // iterate over all blacklist entries
        foreach (var entry in newBlacklist)
        {
            var elementKey = GetKey(entry);
            // if the entry is not in the old blacklist
            if (!oldLookup.Contains(elementKey))
            {
                // add the entry to the blacklist added list
                added.Add(entry);
            }
            else
            {
                // get the old entry
                var oldEntry = oldLookup[elementKey].First();
                // if the entries are not equal
                if (!entry.Equals(oldEntry))
                {
                    // add the entry to the blacklist changed list
                    changed[elementKey] = entry;
                }
            }
        }
        // iterate over all old blacklist entries
        foreach (var entry in oldBlacklist)
        {
            var elementKey = GetKey(entry);
            // if the entry is not in the new blacklist
            if (!newBlacklist.Any(e => GetKey(e) == elementKey))
            {
                // add the entry to the blacklist removed list
                removed.Add(entry);
            }
        }
    }

    private static string GetKey(ListEntry e)
    {
        // filters used to narrow down an item, not affecting actual actions are part of the id
        var relevantFilters = e.filter?.Where(f =>
            !f.Key.Equals("MaxCost", System.StringComparison.OrdinalIgnoreCase)
            && !f.Key.Equals("Minprofit", System.StringComparison.OrdinalIgnoreCase)
            && !f.Key.Equals("profit", System.StringComparison.OrdinalIgnoreCase)
            && !f.Key.Equals("PricePerUnit", System.StringComparison.OrdinalIgnoreCase)
            && !f.Key.Equals("StartingBid", System.StringComparison.OrdinalIgnoreCase)
            && !f.Key.Equals("ProfitPercentage", System.StringComparison.OrdinalIgnoreCase)
            || e.ItemTag == null // no item, all filters are relevant
        ).Select(f => f.Key + "=" + f.Value);
        return e.ItemTag + (e.Tags == null ? string.Empty : string.Join(',', e.Tags))
            + (relevantFilters == null ? string.Empty : string.Join(',', relevantFilters));
    }

    private static void AssignSetCommandsDiff<T>(T oldSettings, T newSettings, SettingsDiff differences, string prefix = "")
    {
        if (oldSettings == null || newSettings == null)
        {
            return;
        }
        foreach (var property in typeof(T).GetFields())
        {
            // if object continue
            if (property.FieldType.IsClass && property.FieldType != typeof(string))
            {
                continue;
            }
            // get the value of the property in the old settings
            var oldValue = property.GetValue(oldSettings);
            // get the value of the property in the new settings
            var newValue = property.GetValue(newSettings);
            var dataMembername = property.GetCustomAttribute<DataMemberAttribute>()?.Name;
            // if the values are not equal
            if (!Equals(oldValue, newValue) && dataMembername != null
                && !dataMembername.Equals(
                    "blockExport", System.StringComparison.OrdinalIgnoreCase))
            {
                // add a set command to the differences
                differences.SetCommands.Add($"{prefix + dataMembername} {newValue}");
            }
        }
    }

    public class SettingsDiff
    {
        public List<string> SetCommands { get; set; } = [];
        public List<ListEntry> BlacklistAdded { get; set; } = [];
        public Dictionary<string, ListEntry> BlacklistChanged { get; set; } = [];
        public List<ListEntry> BlacklistRemoved { get; set; } = [];
        public List<ListEntry> WhitelistAdded { get; set; } = [];
        public Dictionary<string, ListEntry> WhitelistChanged { get; set; } = [];
        public List<ListEntry> WhitelistRemoved { get; set; } = [];
        public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;

        public int GetDiffCount()
        {
            return SetCommands.Count + BlacklistAdded.Count + BlacklistChanged.Count + BlacklistRemoved.Count + WhitelistAdded.Count + WhitelistChanged.Count + WhitelistRemoved.Count;
        }
    }
}
