using System.Collections.Generic;
using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;
public class SettingsDifferTests
{
    [Test]
    public void GetDifferencesTest()
    {
        var oldSettings = new FlipSettings() { ModSettings = new() };
        var newSettings = new FlipSettings
        {
            MinProfit = 1,
            ModSettings = new ModSettings
            {
                Chat = true
            }
        };

        var result = SettingsDiffer.GetDifferences(oldSettings, newSettings);

        result.SetCommands.Should().Contain("minProfit 1");
        result.SetCommands.Should().Contain("modchat True");
    }

    [Test]
    public void AddBlacklist()
    {
        var oldSettings = new FlipSettings() { BlackList = new() };
        var newSettings = new FlipSettings
        {
            BlackList = new List<ListEntry>
            {
                new ListEntry
                {
                    ItemTag = "tag",
                    DisplayName = "name"
                }
            }
        };

        var result = SettingsDiffer.GetDifferences(oldSettings, newSettings);

        result.BlacklistAdded.Should().Contain(new ListEntry
        {
            ItemTag = "tag",
            DisplayName = "name"
        });
    }

    [Test]
    public void EditedBlacklist()
    {
        var oldSettings = new FlipSettings()
        {
            BlackList = new List<ListEntry>
            {
                new ListEntry
                {
                    ItemTag = "tag",
                    DisplayName = "name",
                    filter = new Dictionary<string, string>
                    {
                        { "minprofit", "value" }
                    }, Tags = new List<string> { "xy" }
                }
            }
        };
        var newSettings = new FlipSettings
        {
            BlackList = new List<ListEntry>
            {
                new ListEntry
                {
                    ItemTag = "tag",
                    DisplayName = "name",
                    filter = new Dictionary<string, string>
                    {
                        { "minprofit", "value2" }
                    }, Tags = new List<string> { "xy" }
                }
            }
        };

        var result = SettingsDiffer.GetDifferences(oldSettings, newSettings);

        result.BlacklistChanged.Should().ContainKey("tagxy");
        result.BlacklistChanged["tagxy"].filter.Should()
            .ContainKey("minprofit").WhoseValue.Should().Be("value2");
    }

    [Test]
    public void RemoveNoMatch()
    {
        var oldSettings = new FlipSettings()
        {
            BlackList = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "minprofit", "value" },
                        { "Rarity", "epic" }
                    }
                }
            }
        };
        var newSettings = new FlipSettings
        {
            BlackList = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "minprofit", "value" },
                        { "Rarity", "epic" }
                    }
                }
            }
        };

        var result = SettingsDiffer.GetDifferences(oldSettings, newSettings);

        result.BlacklistRemoved.Should().BeEmpty();
        result.BlacklistChanged.Should().BeEmpty();

        newSettings.BlackList[0].filter["minprofit"] = "value2";

        result = SettingsDiffer.GetDifferences(oldSettings, newSettings);

        result.BlacklistRemoved.Should().BeEmpty();
        result.BlacklistChanged.Should().ContainKey("tagRarity=epic");
    }

    [Test]
    public void RemoveMatch()
    {
        var oldSettings = new FlipSettings()
        {
            BlackList = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "key", "value" }
                    }
                }
            }
        };
        var differ = new SettingsDiffer();
        var result = differ.ApplyDiff(oldSettings, new SettingsDiffer.SettingsDiff()
        {
            BlacklistRemoved = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "key", "value" }
                    }
                }
            }
        });

        result.BlackList.Should().BeEmpty();
    }

    [Test]
    public void DeduplicatesEntriesOnUpdate()
    {
        var oldSettings = new FlipSettings()
        {
            BlackList = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "key", "value" }
                    }
                },
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "key", "value" }
                    }
                }
            }
        };
        var differ = new SettingsDiffer();
        var result = differ.ApplyDiff(oldSettings, new SettingsDiffer.SettingsDiff()
        {
            BlacklistAdded = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "key", "value" }
                    }
                }
            }
        });

        result.BlackList.Should().HaveCount(1);
    }

    [Test]
    public void AddWhitelist()
    {
        var oldSettings = new FlipSettings() { WhiteList = new() };;
        var differ = new SettingsDiffer();
        var diff = new SettingsDiffer.SettingsDiff()
        {
            WhitelistAdded = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    DisplayName = "name"
                }
            }
        };

        var result = differ.ApplyDiff(oldSettings, diff);

        result.WhiteList.Should().Contain(new ListEntry
        {
            ItemTag = "tag",
            DisplayName = "name"
        });
    }

    [Test]
    public void UpdateWhitelistMaxCostE2E()
    {
        var oldSettings = new FlipSettings()
        {
            WhiteList = new List<ListEntry>
            {
                new() {
                    ItemTag = "tag",
                    filter = new Dictionary<string, string>
                    {
                        { "key", "value" }
                    }
                }
            }
        };
        var differ = new SettingsDiffer();
        var diff = new SettingsDiffer.SettingsDiff()
        {
            WhitelistChanged = new Dictionary<string, ListEntry>
            {
                { "tag", new()
                    {
                        ItemTag = "tag",
                        filter = new Dictionary<string, string>
                        {
                            { "key", "value2" }
                        }
                    }
                }
            }
        };

        var result = differ.ApplyDiff(oldSettings, diff);

        result.WhiteList.Should().Contain(new ListEntry
        {
            ItemTag = "tag",
            filter = new Dictionary<string, string>
            {
                { "key", "value2" }
            }
        });
    }
}

