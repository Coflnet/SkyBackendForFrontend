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
    public void GetUserDifferenceConfigExportsOnlyUserAdditionsAndOverrides()
    {
        var expertBlacklist = new ListEntry
        {
            ItemTag = "EXPERT_ITEM",
            filter = new Dictionary<string, string> { { "MinProfit", "1000000" } }
        };
        var expertWhitelist = new ListEntry { ItemTag = "EXPERT_WHITELIST_ITEM" };
        var userBlacklist = new ListEntry { ItemTag = "USER_ITEM" };
        var userWhitelist = new ListEntry { ItemTag = "USER_WHITELIST_ITEM" };
        var currentExpertConfig = new FlipSettings
        {
            MinProfit = 1_000_000,
            MinProfitPercent = 10,
            MinVolume = 2,
            MaxCost = 100_000_000,
            OnlyBin = true,
            BlackList = [expertBlacklist],
            WhiteList = [expertWhitelist]
        };
        var combinedUserConfig = new FlipSettings
        {
            BlockExport = true,
            MinProfit = 2_000_000,
            MinProfitPercent = 15,
            MinVolume = 4,
            MaxCost = 200_000_000,
            OnlyBin = true,
            BlackList = [expertBlacklist, userBlacklist],
            WhiteList = [expertWhitelist, userWhitelist]
        };

        var result = SettingsDiffer.GetUserDifferenceConfig(combinedUserConfig, currentExpertConfig);

        result.MinProfit.Should().Be(2_000_000);
        result.MinProfitPercent.Should().Be(15);
        result.MinVolume.Should().Be(4);
        result.MaxCost.Should().Be(200_000_000);
        result.OnlyBin.Should().BeFalse();
        result.BlockExport.Should().BeFalse();
        result.BlackList.Should().ContainSingle().Which.Should().Be(userBlacklist);
        result.WhiteList.Should().ContainSingle().Which.Should().Be(userWhitelist);
        result.ModSettings.Should().NotBeNull();
        result.Visibility.Should().NotBeNull();
    }

    [Test]
    public void GetUserDifferenceConfigOmitsRemovedExpertEntries()
    {
        var currentExpertConfig = new FlipSettings
        {
            BlackList = [new ListEntry { ItemTag = "EXPERT_ITEM" }],
            WhiteList = []
        };
        var combinedUserConfig = new FlipSettings
        {
            BlackList = [],
            WhiteList = []
        };

        var result = SettingsDiffer.GetUserDifferenceConfig(combinedUserConfig, currentExpertConfig);

        result.BlackList.Should().BeEmpty();
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
