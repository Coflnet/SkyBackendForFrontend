using NUnit.Framework;
using Coflnet.Sky.Core;
using System.Diagnostics;
using Coflnet.Sky.Commands.Tests;
using System.Threading.Tasks;
using Coflnet.Sky.Filter;
using System.Linq;
using FluentAssertions;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.Shared
{
    public class FlipSettingsTests
    {
        private const string SwordTag = "ASPECT_OF_THE_END";
        private const string PetTag = "ASPECT_OF_THE_END";
        private FlipInstance flipA;

        private FlipInstance flipB;
        private FlipSettings settings;

        [SetUp]
        public void Setup()
        {
            DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(new FlipFilterTests.NBTMock()));
            flipA = new FlipInstance()
            {
                Auction = new SaveAuction()
                {
                    Enchantments = new System.Collections.Generic.List<Enchantment>()
                    {
                        new Enchantment(Enchantment.EnchantmentType.critical,6)
                    },
                    Tag = SwordTag
                },
                Volume = 1,
                MedianPrice = 1000,
                Finder = LowPricedAuction.FinderType.SNIPER
            };

            flipB = new FlipInstance()
            {
                Auction = new SaveAuction()
                {
                    Enchantments = new System.Collections.Generic.List<Enchantment>()
                    {
                        new Enchantment(Enchantment.EnchantmentType.sharpness,6)
                    },
                    Tag = SwordTag
                },
                MedianPrice = 500,
                Finder = LowPricedAuction.FinderType.SNIPER
            };
            CreateLookup(flipA);
            CreateLookup(flipB);

            settings = new FlipSettings()
            {
                WhiteList = new System.Collections.Generic.List<ListEntry>()
                {
                    new ListEntry(){ItemTag = SwordTag, filter = new System.Collections.Generic.Dictionary<string, string>()
                    {
                        { "Enchantment","critical"}, {"EnchantLvl", "6"}
                    }},
                    new ListEntry(){ItemTag = PetTag, filter = new System.Collections.Generic.Dictionary<string, string>()
                    {
                        { "PetLevel","1-80"}, {"Rarity", "UNCOMMON"}
                    }}
                },
                BlackList = new System.Collections.Generic.List<ListEntry>()
                {
                    new ListEntry(){ItemTag = SwordTag, filter = new System.Collections.Generic.Dictionary<string, string>()
                    {
                        { "Enchantment","sharpness"}, {"EnchantLvl", "6"}
                    }},
                    new ListEntry(){ItemTag = "A"},
                    new ListEntry(){ItemTag = "A"},
                    new ListEntry(){ItemTag = "b"},
                    new ListEntry(){ItemTag = "o"},
                    new ListEntry(){ItemTag = "i"},
                    new ListEntry(){ItemTag = "u"},
                    new ListEntry(){ItemTag = "Az"},
                    new ListEntry(){ItemTag = "Aq"},
                    new ListEntry(){ItemTag = "Aw"},
                    new ListEntry(){ItemTag = "Ae"},
                    new ListEntry(){ItemTag = "Ar"},
                    new ListEntry(){ItemTag = "At"},
                    new ListEntry(){ItemTag = "Aa"},
                    new ListEntry(){ItemTag = "As"},
                    new ListEntry(){ItemTag = "Ad"},
                    new ListEntry(){ItemTag = "Af"},
                    new ListEntry(){ItemTag = "Ag"},
                    new ListEntry(){ItemTag = "Ay"},
                    new ListEntry(){ItemTag = "Ax"},
                    new ListEntry(){ItemTag = "Ac"},
                    new ListEntry(){ItemTag = PetTag, filter = new System.Collections.Generic.Dictionary<string, string>()
                    {
                        { "PetLevel","<90"}, {"Rarity", "COMMON"}
                    }}
                },
            };
        }

        private static void CreateLookup(FlipInstance flip)
        {
            flip.Auction.NBTLookup = new[] { new NBTLookup(1, 2) };
        }

        [Test]
        public void MatchesSettings1()
        {
            Assert.That(settings.MatchesSettings(flipA).Item1);
            var bRes = settings.MatchesSettings(flipB);
            Assert.That(!bRes.Item1, bRes.Item2);
            flipB.Auction.Enchantments.AddRange(flipA.Auction.Enchantments);
            bRes = settings.MatchesSettings(flipB);
            Assert.That(bRes.Item1, bRes.Item2);
        }
        [Test]
        public void BlacklitPetLevel()
        {
            flipB.Auction.Tag = PetTag;
            flipB.Auction.NBTLookup = new[] { new NBTLookup(1, 2) };
            var bRes = settings.MatchesSettings(flipB);
            Assert.That(!bRes.Item1, bRes.Item2);
        }
        [TestCase("STARRED_TEST", "TEST")]
        [TestCase("STARRED_TEST", "STARRED_TEST")]
        [TestCase("TEST", "STARRED_TEST")]
        [TestCase("TEST", "TEST")]
        public void WhitelistStarred(string tag, string whitelistTag)
        {
            flipB.Auction.Tag = tag;
            var bRes = settings.MatchesSettings(flipB);
            Assert.That(!bRes.Item1, bRes.Item2);
            settings = new FlipSettings
            {
                WhiteList =
                [
                    new ListEntry(){ItemTag = whitelistTag, filter = new System.Collections.Generic.Dictionary<string, string>()
                        {
                            { "sharpness","6"}
                        }}
                ],
                MinProfit = 1_000_000_000
            };
            settings.WhiteList[0].ItemTag = whitelistTag;
            bRes = settings.MatchesSettings(flipB);
            Assert.That(bRes.Item1, bRes.Item2);
        }

        [Test]
        public async Task ConcurrentTest()
        {
            settings.ClearListMatchers();
            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    settings.MatchesSettings(flipA);
                });
            }
            await Task.WhenAll(tasks);
        }

        [Test]
        public void MatchesSpeed()
        {
            var hostName = System.Net.Dns.GetHostName();
            if (hostName.Contains("-build"))
                Assert.Ignore("Running on build server");
            var matchCount = 0;
            var iterations = 2000;
            settings.MatchesSettings(flipB); // compile
            var stopWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                if (settings.MatchesSettings(flipA).Item1)
                    matchCount++;
            }
            Assert.That(6 * TestConstants.DelayMultiplier, Is.GreaterThan(stopWatch.ElapsedMilliseconds), "matching is too slow");
            stopWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                if (!settings.MatchesSettings(flipB).Item1)
                    matchCount++;
            }
            Assert.That(140 * TestConstants.DelayMultiplier, Is.GreaterThan(stopWatch.ElapsedMilliseconds), "matching blacklist is too slow");
            Assert.That(iterations * 2, Is.EqualTo(matchCount));
        }


        [Test]
        public void CreateListMatcherWithNull()
        {
            var matcher = new FlipSettings.ListMatcher(null, null, new(), System.Threading.CancellationToken.None);
            Assert.That(!matcher.IsMatch(flipA).Item1);
        }
        [Test]
        public void PreProcessor()
        {
            var matcher = new FlipSettings.ListMatcher(new(){
                new()
                {
                    filter = new()
                    {
                        { "Enchantment","critical"}, {"EnchantLvl", "6"},
                        { "ForTag", "Xy"}
                    }
                },
                new()
                {
                    ItemTag = "A",
                    Tags = new() { "Xy" },
                    filter = new()
                    {
                    }
                },
            }, null, new(), System.Threading.CancellationToken.None);
            Assert.That(1, Is.EqualTo(matcher.FullList.Count));
            Assert.That(1, Is.EqualTo(matcher.FullList[0].Tags.Count));
            Assert.That(2, Is.EqualTo(matcher.FullList[0].filter.Count));
        }

        [TestCase(">{{MIN_PROFIT}}*0.5")]
        [TestCase("{{MIN_PROFIT}}*0.5-{{MIN_PROFIT}}*1.5")]
        [TestCase("{{MIN_PROFIT}}*0.776")]
        public void Variables(string expression)
        {
            var settings = new FlipSettings()
            {
                MinProfit = 1_000_000,
                WhiteList = [
                    new ListEntry(){ItemTag = SwordTag, filter = new System.Collections.Generic.Dictionary<string, string>()
                        {
                            { "Profit",expression}
                        }}
                ],
                AllowedFinders = LowPricedAuction.FinderType.SNIPER
            };
            flipA.Target = 800_000; // lower than 1m, with current fee rate yields profit matching test cases
            var matches = settings.MatchesSettings(flipA);
            matches.Item1.Should().BeTrue();
            matches.Item2.Should().Be("whitelist matched filter for item");
        }

        [Test]
        public void ReportFromartificialair()
        {
            var listEntry = new ListEntry()
            {
                ItemTag = "SORROW_HELMET",
                filter = new System.Collections.Generic.Dictionary<string, string>()
                {
                    { "Reforge","Renowned"}, {"Enchantment","ultimate_legion"}, {"EnchantLvl","1-5"}, {"ProfitPercentage","1-22"}
                }
            };

            var auction = new FlipInstance()
            {
                MedianPrice = 32_000_000,
                Auction = new SaveAuction()
                {
                    Tag = "SORROW_HELMET",
                    Enchantments = new System.Collections.Generic.List<Enchantment>()
                    {
                        new Enchantment()
                        {
                            Type = Enchantment.EnchantmentType.ultimate_legion,
                            Level = 4
                        }
                    },
                    FlatenedNBT = new System.Collections.Generic.Dictionary<string, string>()
                    {
                        { "rarity_upgrades", "1" },
                        { "hpc", "15" },
                        { "unlocked_slots", "JADE_0" },
                        { "uid", "83de32552763" }
                    },
                    StartingBid = 28_000_000,
                    Reforge = ItemReferences.Reforge.Renowned
                }
            };

            var profitp = auction.ProfitPercentage;

            Assert.That(listEntry.MatchesSettings(auction, null));
        }
        [Test]
        public void MinProfitPercentageForHyperion()
        {
            var listEntry = new ListEntry()
            {
                ItemTag = "HYPERION",
                filter = new System.Collections.Generic.Dictionary<string, string>()
                {
                    { "FlipFinder", "SNIPER_MEDIAN"}, {"MinProfitPercentage", "60"}
                }
            };

            var auction = new FlipInstance()
            {
                MedianPrice = 820000000,
                Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN,
                Auction = new SaveAuction()
                {
                    Tag = "HYPERION",
                    StartingBid = 1000000,
                    Reforge = ItemReferences.Reforge.Heroic
                }
            };
            // report https://discord.com/channels/267680588666896385/986382602376196116/986382624362737685
            Assert.That(listEntry.MatchesSettings(auction, null));
        }

        [Test]
        public void IsFinderBlockedDefault()
        {
            var settings = new FlipSettings() { };
            Assert.That(settings.IsFinderBlocked(LowPricedAuction.FinderType.USER));
            Assert.That(settings.IsFinderBlocked(LowPricedAuction.FinderType.FLIPPER));
            Assert.That(settings.IsFinderBlocked(LowPricedAuction.FinderType.STONKS));
            Assert.That(settings.IsFinderBlocked(LowPricedAuction.FinderType.TFM));
            Assert.That(!settings.IsFinderBlocked(LowPricedAuction.FinderType.SNIPER));
            Assert.That(!settings.IsFinderBlocked(LowPricedAuction.FinderType.SNIPER_MEDIAN));
        }
        [Test]
        public void IsFinderBlocked()
        {
            var settings = new FlipSettings() { AllowedFinders = LowPricedAuction.FinderType.FLIPPER | LowPricedAuction.FinderType.SNIPER };
            Assert.That(settings.IsFinderBlocked(LowPricedAuction.FinderType.USER));
            Assert.That(settings.IsFinderBlocked(LowPricedAuction.FinderType.SNIPER_MEDIAN));
            Assert.That(!settings.IsFinderBlocked(LowPricedAuction.FinderType.SNIPER));
            Assert.That(!settings.IsFinderBlocked(LowPricedAuction.FinderType.FLIPPER));
        }

        [Test]
        public void NoDupplicateShortHand()
        {
            var updater = new SettingsUpdater();
            var options = updater.Options();
        }
    }
}
