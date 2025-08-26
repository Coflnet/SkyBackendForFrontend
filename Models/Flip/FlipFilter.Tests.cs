
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Coflnet.Sky.Commands.Tests;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared
{
    public class FlipFilterTests
    {
        FlipInstance sampleFlip;

        [SetUp]
        public void Setup()
        {
            DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(new NBTMock()));
            sampleFlip = new FlipInstance()
            {
                MedianPrice = 10,
                Volume = 10,
                Auction = new SaveAuction()
                {
                    Bin = false,
                    Enchantments = new List<Enchantment>(){
                    new(Enchantment.EnchantmentType.critical,4)
                },
                    FlatenedNBT = new Dictionary<string, string>() { { "candy", "3" } }
                },
                Context = new Dictionary<string, string>(),
                Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
        }

        [Test]
        public void DoesNotMatchBigBrain5()
        {
            var auction = """
            {"enchantments":[{"color":"§d","value":17293216,"type":"ultimate_soul_eater","level":5},{"color":"§5","value":13785139,"type":"overload","level":5},
            {"color":"§5","value":9999997,"type":"dragon_hunter","level":4},{"color":"§5","value":99458,"type":"power","level":6},
            {"color":"§5","value":57633,"type":"infinite_quiver","level":10},{"color":"§9","value":-1,"type":"impaling","level":3},
            {"color":"§9","value":-1,"type":"chance","level":3},{"color":"§9","value":-1,"type":"piercing","level":1},{"color":"§9","value":-1,"type":"telekinesis","level":1},
            {"color":"§9","value":-1,"type":"snipe","level":3},{"color":"§9","value":-1,"type":"punch","level":2},{"color":"§9","value":-1,"type":"flame","level":2},
            {"color":"§9","value":-1,"type":"aiming","level":5},{"color":"§9","value":-1,"type":"cubism","level":5}],
            "uuid":"26c6b4b15fd44eafa249c2f4721ce58e","count":1,"startingBid":64000000,"tag":"JUJU_SHORTBOW","itemName":"Spiritual Juju Shortbow ✪✪✪✪✪",
            "start":"2024-09-13T09:57:31","end":"2024-09-13T09:57:50","auctioneerId":"1b4327dd25bf42f1bd8db5295932f7a8",
            "profileId":"723c00cd08644d2aaad26fb5c2c08108","coop":null,"coopMembers":null,"highestBidAmount":64000000,
            "bids":[{"bidder":"5058f327c0284938b66a7c436b496460","profileId":"cd30f581b5d346bda043ffb9c576ce51","amount":64000000,"timestamp":"2024-09-13T09:57:54"}],
            "anvilUses":0,"nbtData":{"data":{"rarity_upgrades":1,"stats_book":123351,"hpc":15,"dungeon_item_level":5,"uid":"fe60bd663205","uuid":"97a9d78e-3944-499f-99b6-fe60bd663205"}},
            "itemCreatedAt":"2021-12-08T21:06:00","reforge":"Spiritual","category":"WEAPON","tier":"LEGENDARY","bin":true,
            "flatNbt":{"rarity_upgrades":"1","stats_book":"123351","hpc":"15","dungeon_item_level":"5","uid":"fe60bd663205","uuid":"97a9d78e-3944-499f-99b6-fe60bd663205"}}
            """;
            var parsed = JsonConvert.DeserializeObject<Core.SaveAuction>(auction);
            var flip = new FlipInstance()
            {
                MedianPrice = 10,
                Volume = 10,
                Auction = parsed,
                Context = new Dictionary<string, string>(),
                Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
            var settings = new FlipSettings()
            {
                MinProfit = 10000,
                WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() {
                    { "MinProfit", "5" }, {"big_brain", "5-5"} } } }
            };
            NoMatch(settings, flip);
        }

        [Test]
        public void IsMatch()
        {
            var settings = new FlipSettings()
            {
                BlackList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() { { "Bin", "true" } } } }
            };
            var matches = settings.MatchesSettings(sampleFlip);
            Assert.That(matches.Item1, "flip should match");
            sampleFlip.Auction.Bin = true;
            Assert.That(!settings.MatchesSettings(sampleFlip).Item1, "flip should not match");
        }


        [Test]
        public void EnchantmentMatch()
        {
            var settings = new FlipSettings()
            {
                BlackList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() { { "Enchantment", "aiming" }, { "EnchantLvl", "1" } } } }
            };
            var matches = settings.MatchesSettings(sampleFlip);
            Assert.That(matches.Item1, "flip should match");
        }


        [Test]
        public void EnchantmentBlacklistMatch()
        {
            var settings = new FlipSettings()
            {
                BlackList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() { { "Enchantment", "critical" }, { "EnchantLvl", "4" } } } }
            };
            var matches = settings.MatchesSettings(sampleFlip);
            Assert.That(!matches.Item1, "flip should not match");
        }

        [Test]
        public void CandyBlacklistMatch()
        {
            sampleFlip.Auction.FlatenedNBT["candyUsed"] = "1";
            var settings = new FlipSettings()
            {
                BlackList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() { { "Candy", "any" } } } }
            };
            var matches = settings.MatchesSettings(sampleFlip);
            Console.WriteLine(new FilterEngine(new NBTMock()).GetMatchExpression(settings.BlackList[0].filter).ToString());
            Assert.That(!matches.Item1, "flip should not match " + matches.Item2);
            sampleFlip.Auction.FlatenedNBT["candyUsed"] = "0";
            matches = settings.MatchesSettings(sampleFlip);
            Assert.That(matches.Item1, "flip should match " + matches.Item2);
        }

        [Test]
        public void WhitelistBookEnchantBlackistItem()
        {
            var tag = "ENCHANTED_BOOK";
            FlipInstance bookOfa = CreatOfaAuction(tag);
            FlipInstance reaperOfa = CreatOfaAuction("REAPER");
            var oneForAllFilter = new Dictionary<string, string>() { { "Enchantment", "ultimate_one_for_all" }, { "EnchantLvl", "1" } };
            var settings = new FlipSettings()
            {
                BlackList = new List<ListEntry>() { new() { ItemTag = "REAPER", filter = oneForAllFilter } },
                WhiteList = new List<ListEntry>() { new() { ItemTag = "ENCHANTED_BOOK", filter = oneForAllFilter } }
            };
            var matches = settings.MatchesSettings(bookOfa);
            var shouldNotBatch = settings.MatchesSettings(reaperOfa);
            Assert.That(matches.Item1, "flip should match");
            Assert.That(!shouldNotBatch.Item1, "flip should not match");
        }


        [Test]
        public void MinProfitFilterMatch()
        {
            sampleFlip.Auction.NBTLookup = new NBTLookup[] { new(1, 2) };
            var settings = new FlipSettings()
            {
                MinProfit = 10000,
                WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() { { "MinProfit", "5" } } } }
            };
            var matches = settings.MatchesSettings(sampleFlip);
            System.Console.WriteLine(sampleFlip.Profit);
            Assert.That(matches.Item1, matches.Item2);
        }



        [Test]
        public void VolumeDeciamalFilterMatch()
        {
            sampleFlip.Auction.NBTLookup = new NBTLookup[] { new(1, 2) };
            var settings = new FlipSettings()
            {
                MinProfit = 1,
                MinVolume = 0.5,

            };
            sampleFlip.Volume = 0.8f;
            var matches = settings.MatchesSettings(sampleFlip);
            Assert.That(matches.Item1, matches.Item2);
            sampleFlip.Volume = 0.2f;
            var matches2 = settings.MatchesSettings(sampleFlip);
            Assert.That(!matches2.Item1, matches2.Item2);
        }

        [Test]
        public void VolumeDeciamalFilterWhitelistMatch()
        {
            var settings = new FlipSettings()
            {
                MinProfit = 1,
                MinVolume = 50,
            };
            settings.WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() { { "Volume", "<0.5" } } } };
            sampleFlip.Volume = 0.1f;
            var matches3 = settings.MatchesSettings(sampleFlip);
            Assert.That(matches3.Item1, matches3.Item2);
            sampleFlip.Volume = 1;
            var notMatch = settings.MatchesSettings(sampleFlip);
            Assert.That(!notMatch.Item1, notMatch.Item2);
        }

        [Test]
        [TestCase("1", 1, true)]
        [TestCase("<1", 0.5f, true)]
        [TestCase(">1", 0.5f, false)]
        [TestCase("<0.5", 0.1f, true)]
        public void VolumeDeciamalFilterSingleMatch(string val, float vol, bool result)
        {
            var volumeFilter = new VolumeDetailedFlipFilter();
            var exp = volumeFilter.GetExpression(null, val);
            Assert.That(exp.Compile().Invoke(new FlipInstance() { Volume = vol }), Is.EqualTo(result));
        }
        [Test]
        [TestCase("1", true)]
        [TestCase("2", false)]
        public void ReferenceAgeFilterMatch(string val, bool result)
        {
            var settings = new FlipSettings
            {
                MinProfit = 100,
                WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() { { "ReferenceAge", "<2" } } } }
            };
            sampleFlip.Context["refAge"] = val;
            var matches3 = settings.MatchesSettings(sampleFlip);
            Assert.That(result, Is.EqualTo(matches3.Item1), matches3.Item2);
        }



        [Test]
        public void FlipFilterFinderCustomMinProfitNoBinMatch()
        {
            var settings = new FlipSettings()
            {
                MinProfit = 10000,
                WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() {
                    { "MinProfit", "5" },{"FlipFinder", "SNIPER_MEDIAN"},{"Bin","false"} } } }
            };
            sampleFlip.Auction.StartingBid = 10;
            sampleFlip.MedianPrice = 100;
            sampleFlip.Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN;
            Matches(settings, sampleFlip);
            sampleFlip.Finder = LowPricedAuction.FinderType.FLIPPER;
            NoMatch(settings, sampleFlip);
        }
        [Test]
        public void FlipFilterFinderCustomMinProfitMatch()
        {
            var settings = new FlipSettings()
            {
                MinProfit = 10000,
                WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() {
                    { "MinProfit", "5" } } } }
            };
            sampleFlip.Auction.StartingBid = 10;
            sampleFlip.MedianPrice = 100;
            sampleFlip.Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN;
            Matches(settings, sampleFlip);
        }

        [Test]
        public void RenownedFivestaredMythic()
        {
            var filters = new Dictionary<string, string>() { { "Stars", "5" }, { "Reforge", "Renowned" }, { "Rarity", "MYTHIC" } };
            var matcher = new ListEntry() { filter = filters, ItemTag = "abc" };
            var result = matcher.GetExpression(null).Compile()(new FlipInstance()
            {
                Auction = new SaveAuction()
                {
                    Reforge = ItemReferences.Reforge.Renowned,
                    Tier = Tier.MYTHIC,
                    FlatenedNBT = new Dictionary<string, string>() { { "upgrade_level", "5" } }
                }
            });
            Assert.That(result);
        }

        [Test]
        public void WhitelistAfterMain()
        {
            var settings = new FlipSettings()
            {
                WhiteList = new List<ListEntry>() { new() { filter = new() { { "Reforge", "Sharp" }, { "AfterMainFilter", "true" } } } },
                MinProfit = 1000
            };
            sampleFlip.Auction.Reforge = ItemReferences.Reforge.Sharp;
            sampleFlip.Auction.StartingBid = 5;
            sampleFlip.MedianPrice = 500;
            var matches = settings.MatchesSettings(sampleFlip);
            Assert.That(!matches.Item1, "flip shouldn't match below minprofit");
            sampleFlip.MedianPrice = 5000;
            matches = settings.MatchesSettings(sampleFlip);
            Assert.That(matches.Item1, "flip should match above minprofit");
        }


        [Test]
        public void ForceBlacklistOverwritesWhitelist()
        {
            var settings = new FlipSettings
            {
                MinProfit = 0,
                MinVolume = 0,
                WhiteList = new List<ListEntry>() { new() { filter = new() { { "Volume", "<0.5" } } } },
                BlackList = new List<ListEntry>() { new() { filter = new() { { "Volume", "<0.5" }, { "ForceBlacklist", "" } } } }
            };
            sampleFlip.Volume = 0.1f;
            var result = settings.MatchesSettings(sampleFlip);

            Assert.That(!result.Item1, result.Item2);
            Assert.That("forced blacklist matched general filter", Is.EqualTo(result.Item2));
        }

        [Test]
        public void JujuHighProfit()
        {
            var settings = new FlipSettings
            {
                MinProfit = 0,
                MinVolume = 0,
                WhiteList = new List<ListEntry>() { new() { filter = new() { { "FlipFinder", "SNIPER_MEDIAN" }, { "MinProfitPercentage", "40" } }, ItemTag = "JUJU_SHORTBOW" } },
                BlackList = new List<ListEntry>() { new() { filter = new() { { "FlipFinder", "SNIPER_MEDIAN" } } } }
            };
            sampleFlip.Volume = 0.1f;
            sampleFlip.Auction.Tag = "JUJU_SHORTBOW";
            sampleFlip.MedianPrice = 35800000;
            sampleFlip.Auction.StartingBid = 6000;
            sampleFlip.Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN;
            var result = settings.MatchesSettings(sampleFlip);

            Assert.That(result.Item1, result.Item2);
            Assert.That("whitelist matched filter for item", Is.EqualTo(result.Item2));
        }

        [Test]
        public void FlipFilterFinderBlacklist()
        {
            var settings = new FlipSettings()
            {
                MinProfit = 100,
                BlackList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() {
                    { "FlipFinder", "FLIPPER" } } } }
            };
            sampleFlip.Auction.StartingBid = 10;
            sampleFlip.MedianPrice = 1000000;
            sampleFlip.Finder = LowPricedAuction.FinderType.FLIPPER;
            NoMatch(settings, sampleFlip);
        }
        [Test]
        public void MinProfitPercentage()
        {
            var settings = new FlipSettings()
            {
                MinProfit = 10000,
                WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() {
                    { "ProfitPercentage", ">5" } }
                } }
            };
            sampleFlip.Auction.StartingBid = 10;
            sampleFlip.MedianPrice = 10000;
            Matches(settings, sampleFlip);
        }
        [Test]
        public void RangeProfitPercentage()
        {
            var settings = new FlipSettings()
            {
                MinProfit = 10000,
                WhiteList = new List<ListEntry>() { new() { filter = new Dictionary<string, string>() {
                    { "ProfitPercentage", "1-10" } }
                } }
            };
            sampleFlip.Auction.StartingBid = 50;
            sampleFlip.MedianPrice = 55;
            Console.WriteLine(sampleFlip.ProfitPercentage);
            Matches(settings, sampleFlip);
        }

        [Test]
        public void CheckCombinedFinder()
        {
            var settings = new FlipSettings()
            {
                MinProfit = 35000000,
                WhiteList = new List<ListEntry>() { new() { ItemTag = "PET_ENDER_DRAGON",
                filter = new Dictionary<string, string>() {
                    { "FlipFinder", "FLIPPER_AND_SNIPERS" },
                    { "MinProfitPercentage", "5" }
                } } },
                BlackList = new List<ListEntry>() { new() { ItemTag = "PET_ENDER_DRAGON" } }
            };
            sampleFlip.Auction.Tag = "PET_ENDER_DRAGON";
            sampleFlip.Auction.StartingBid = 250000000;
            sampleFlip.MedianPrice = 559559559;
            sampleFlip.Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN;
            Matches(settings, sampleFlip);
        }


        [Test]
        public void CheckForError()
        {
            DiHandler.AddTestServices();
            var settings = JsonConvert.DeserializeObject<FlipSettings>(SpiritSettings);
            settings.WhiteList = [];
            foreach (var entry in settings.BlackList.ToList())
            {
                if (!(entry.filter?.TryGetValue("ForceBlacklist", out var force) ?? false) || force != "true")
                    settings.BlackList.Remove(entry);
            }
            File.WriteAllText("test.json", JsonConvert.SerializeObject(settings, Formatting.Indented));
            Matches(settings, new FlipInstance()
            {
                Auction = new SaveAuction()
                {
                    ItemName = "test",
                    Tag = "test",
                    Bin = true,
                    StartingBid = 2,
                    NBTLookup = Array.Empty<NBTLookup>(),
                    FlatenedNBT = new(),
                    Enchantments = new(),
                    Context = new()
                },
                Finder = LowPricedAuction.FinderType.SNIPER,
                MedianPrice = 100000000,
                LowestBin = 100000,
                Context = new()
            });
        }

        private static void Matches(FlipSettings targetSettings, FlipInstance flip)
        {
            var matches = targetSettings.MatchesSettings(flip);
            Assert.That(matches.Item1, matches.Item2);
        }
        private static void NoMatch(FlipSettings targetSettings, FlipInstance flip)
        {
            var matches = targetSettings.MatchesSettings(flip);
            Assert.That(!matches.Item1, matches.Item2);
        }

        private static ListEntry CreateFilter(string key, string value)
        {
            return new ListEntry() { filter = new Dictionary<string, string>() { { key, value } } };
        }

        private static FlipInstance CreatOfaAuction(string tag)
        {
            return new FlipInstance()
            {
                MedianPrice = 10,
                Volume = 10,
                Auction = new SaveAuction()
                {
                    Tag = tag,
                    Enchantments = new List<Enchantment>(){
                        new(Enchantment.EnchantmentType.ultimate_one_for_all,1)
                    }
                },
                Finder = LowPricedAuction.FinderType.SNIPER
            };
        }

        public class NBTMock : INBT
        {
            public NBTLookup[] CreateLookup(string auctionTag, Dictionary<string, object> data, List<KeyValuePair<string, object>> flatList = null)
            {
                throw new NotImplementedException();
            }

            public NBTLookup[] CreateLookup(SaveAuction auction)
            {
                throw new NotImplementedException();
            }

            public long GetItemIdForSkin(string name)
            {
                throw new NotImplementedException();
            }

            public short GetKeyId(string name)
            {
                return 1;
            }

            public int GetValueId(short key, string value)
            {
                return 2;
            }
        }



        private string SpiritSettings = """
        {
    "filters": null,
    "blacklist": [
        {
            "tag": "POTATO",
            "displayName": "Potatoes",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_PHOENIX",
            "displayName": "Phoenix",
            "filter": {
                "ProfitPercentage": "<17",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Wisp",
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true",
                "Profit": "<25000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FINAL_DESTINATION",
                "ProfitPercentage": "<18",
                "EmanKills": ">20k",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BAG_OF_CASH",
            "displayName": "Bag of Cash",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CRYPT_DREADLORD_SWORD",
            "displayName": "Dreadlord Sword",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "STARRED_SHADOW_ASSASSIN",
                "ForceBlacklist": "true",
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "FlipFinder": "STONKS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "62ff2f17d5a845168ba7999912c8958c",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DWARF_TURTLE_SHELMET",
            "displayName": "Dwarf Turtle Shelmet",
            "filter": {
                "ForceBlacklist": "true",
                "Recombobulated": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ARTISANAL_SHORTBOW",
            "displayName": "Artisanal Shortbow",
            "filter": {},
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "sandstone slab",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Seller": "87f2b48f5aeb4a79a486d77f53680157"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Seller": "6bd68ee0d9604dc19ecb5a021ce75299"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LAVA",
            "displayName": "Lava (No Spread)",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER_2",
            "displayName": "Pumpkin Dicer 2.0",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STATIONARY_WATER",
            "displayName": "Water",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "Rarity": "UNCOMMON",
                "ForceBlacklist": "true",
                "PetLevel": "1-100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "bcbf619245f3483fabd15ab179def619",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_PHOENIX",
            "displayName": "Phoenix",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MEGALODON",
            "displayName": "Megalodon",
            "filter": {
                "CurrentMayor": "Marina"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDER_DRAGON",
            "displayName": "Ender Dragon",
            "filter": {
                "PetItem": "PET_ITEM_TIER_BOOST",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SKELETON_MASTER_CHESTPLATE",
            "displayName": "Skeleton Master Chestplate",
            "filter": {},
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "CurrentMayor": "Diana",
                "LastMayor": "Diana",
                "NextMayor": "Diana"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volatility": "75",
                "StartingBid": "50000000",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PetSkin": "Any",
                "Recombobulated": "true",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PetItem": "Any",
                "Recombobulated": "true",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_DRILL_3",
            "displayName": "Topaz Drill KGR-12",
            "filter": {
                "MinProfit": "5000000",
                "MinProfitPercentage": "17"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDER_DRAGON",
            "displayName": "Ender Dragon",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PORTALIZER",
            "displayName": "Portalizer",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STONK_PICKAXE",
            "displayName": "Stonk",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "POISON_SAMPLE",
            "displayName": "Poison Sample",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FEL_ROSE",
            "displayName": "Fel Rose",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BURSTSTOPPER_TALISMAN",
            "displayName": "Burststopper Talisman",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECROMANCER_SWORD",
            "displayName": "Necromancer Sword",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_CORE",
            "displayName": "Magma Core",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MOSQUITO_BOW",
            "displayName": "Mosquito Bow",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MOOSHROOM_COW",
            "displayName": "Mooshroom Cow",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "YETI_ROD",
            "displayName": "Yeti Rod",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SQUID",
            "displayName": "Squid",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RAT",
            "displayName": "Rat",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GLOWSTONE_GAUNTLET",
            "displayName": "Glowstone Gauntlet",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GYROKINETIC_WAND",
            "displayName": "Gyrokinetic Wand",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ATMOSPHERIC_FILTER",
            "displayName": "Atmospheric Filter",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STONE_BLADE",
            "displayName": "Adaptive Blade",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LIVID_DAGGER",
            "displayName": "Livid Dagger",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GLACIAL_SCYTHE",
            "displayName": "Glacial Scythe",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SOULWEAVER_GLOVES",
            "displayName": "Soulweaver Gloves",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TREECAPITATOR_AXE",
            "displayName": "Treecapitator",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TREASURE_RING",
            "displayName": "Treasure Ring",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "KAT_FLOWER",
            "displayName": "Kat Flower",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_TIGER",
            "displayName": "Tiger",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMAN",
            "displayName": "Enderman",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FIRE_VEIL_WAND",
            "displayName": "Fire Veil Wand",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MELON_DICER",
            "displayName": "Melon Dicer",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MELON_DICER_3",
            "displayName": "Melon Dicer 3.0",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARTY_HAT_CRAB_ANIMATED",
            "displayName": "Crab Hat of Celebration - 2022 Edition",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ELEGANT_TUXEDO_CHESTPLATE",
            "displayName": "Elegant Tuxedo Jacket",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WITHER_GOGGLES",
            "displayName": "Wither Goggles",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "EMPTY_CHUMCAP_BUCKET",
            "displayName": "Empty Chumcap Bucket",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CACTUS_KNIFE",
            "displayName": "Cactus Knife",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_ARTIFACT",
            "displayName": "Fermento Artifact",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "abicase"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_DOLPHIN",
            "displayName": "Dolphin",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BEE",
            "displayName": "Bee",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BAL",
            "displayName": "Bal",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BURSTMAW_DAGGER",
            "displayName": "Mawdredge Dagger",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": {
                "Clean": "yes",
                "CurrentMayor": "Diana"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Enchanted Book Bundle"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "GLACITE"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "ForceBlacklist": "true",
                "Candy": "1-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "ArmorDye",
                "ForceBlacklist": "true",
                "FlipFinder": "SNIPER_MEDIAN"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SPIDER",
            "displayName": "Spider",
            "filter": {
                "PetLevel": "1"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "Vanilla",
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ItemCategory": "ISLAND_CRYSTAL"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET_SKIN",
                "Recombobulated": "true",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "RUNE",
                "Recombobulated": "true",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIAMOND_PICKAXE",
            "displayName": "Diamond Pickaxe",
            "filter": {
                "FlipFinder": "TFM",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "WITHER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TITANIUM_DRILL_3",
            "displayName": "Titanium Drill DR-X555",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "TFM"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TITANIUM_DRILL_2",
            "displayName": "Titanium Drill DR-X455",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "TFM"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Enchantment": "dedication",
                "EnchantLvl": "4-4",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_CARMINE",
            "displayName": "Carmine Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [
                "temp",
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_NECRON",
            "displayName": "Necron Dye",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_BRICK_RED",
            "displayName": "Brick Red Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_FLAME",
            "displayName": "Flame Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_MANGO",
            "displayName": "Mango Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_NYANZA",
            "displayName": "Nyanza Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_CELADON",
            "displayName": "Celadon Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_EMERALD",
            "displayName": "Emerald Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_ICEBERG",
            "displayName": "Iceberg Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TENTACLE_DYE",
            "displayName": "Tentacle Dye",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_MIDNIGHT",
            "displayName": "Midnight Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_BYZANTIUM",
            "displayName": "Byzantium Dye",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ProfitPercentage": "<200"
            },
            "tags": [
                "dye",
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_WILD_STRAWBERRY",
            "displayName": "Wild Strawberry Dye",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_NADESHIKO",
            "displayName": "Nadeshiko Dye",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_BONE",
            "displayName": "Bone Dye",
            "filter": {
                "FlipFinder": "ALL_EXCEPT_USER",
                "ProfitPercentage": "<300"
            },
            "tags": [
                "dye",
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_GAUNTLET",
            "displayName": "Gemstone Gauntlet",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<33"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "NullNamed",
                "ForceBlacklist": "true"
            },
            "tags": [
                "GeneralBlacklists"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Shadow Assassin",
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "IntroductionAgeDays": "7",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Shimmer",
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REAPER_SCYTHE",
            "displayName": "Reaper Scythe",
            "filter": {
                "ProfitPercentage": "<25"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Vampire",
                "ProfitPercentage": "<33",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Tuxedo",
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FROZEN_BLAZE",
                "Volume": "<0.6",
                "ProfitPercentage": "<50",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "AXE_OF_THE_SHREDDED",
            "displayName": "Halberd of the Shredded",
            "filter": {
                "ProfitPercentage": "<17",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "AXE_OF_THE_SHREDDED",
            "displayName": "Halberd of the Shredded",
            "filter": {
                "ultimate_wise": "5-5",
                "FlipFinder": "STONKS",
                "ProfitPercentage": "<30",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FINAL_DESTINATION",
                "ProfitPercentage": "<18",
                "EmanKills": ">25k",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "839271a6a485403492fb96f98ff620c1",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "eed3f7c6d1e0455a83baf16997750d00",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "f3f4e40b04004c08a4eab80ba075323c",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "534b8b84527049b189d8608be2459e01",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "075dcaac0ed24c67a66b511fd10df418",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "978d686b34084f7197f65d62cb36f89f",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "6fe4d57e453e4deb959be75a8321f80b",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "2d0213f084784b68ab47fcd58c5190d3",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "e6d4ba1fa15442cd92e52e3dda6cd844",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ExoticColor": "Any:EBEB81,FF43A4,4DCC4D,F06766,D4D4D4,034150,13561E,334CB2,545454,87FF37,46343A,FFF9EB,D400FF,808080,FC2F3C,332A2A,0B004F,D9D9D9,BB6535,FFFF00,FEFDFC,03430E,3C6746,0C4A16,83B03B,7B3F00,04CFD3,268105,7AE82C,F7DA33,117391,9B01C1,7A2900,94451F,ED6612,CBD2DB,C7C7C7,4A14B7,00FF00,B3B3B3,191919,606060,FF0000,6A9C1B,58890C,5D2FB9,6F0F08,CE2C2C,DEBC15,24DDE5,9F8609,E0FCF7,D51230,383838,BFBFBF,993399,E6E6E6,7A7964,276114,FFDC51,C13C0F,5A6464,07A674,17BF89,A82B76,E3FFFA,F04729,FFF6A3,FFCB0D,828282,ADFF2F,FF9300,00BE00,FFD700,9E7003,47D147,017D31,7C44EC,5D23D1,3E05AF,45413C,899E20,6184FC,3F56FB,E65300,2841F1,65605A,E66105,99978B,88837E,FF6F0C,F25D18,1793C4,1CD4E4,17A8C4,C83200,E1EB34,FF6B0B,37B042,CC5500,FFBC0B,8969C8,F0E6AA,FF75FF,CCE5FF,370147,D07F00,1B1B1B,FFFFFF,400352,F0D124,F2DF11,E09419,0000FF,1D1105,E76E3C,A0DAEF,BFBCB2,D91E41,E75C3C,002CA6,0A0011,E7413C,B212E3,03FCF8,DDE4F0,29F0E9",
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "a8af550c75ea4e3a932551dac7832d6a",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "164f3e487e324349a2807d65d153e79c",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "f16dc09735a643538787b70dedd14574",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "eee58ea120c74bd9bf7524643d15d8b2",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "04d725a8bbe44763b1bddd21926d3a66",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TRAINING_DUMMY",
            "displayName": "Training Dummy",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUSPICIOUS_STEW",
            "displayName": "Suspicious Stew",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CRYPT_BOW",
            "displayName": "Soulstealer Bow",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FAIRY",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RAT",
            "displayName": "Rat",
            "filter": {
                "Rarity": "MYTHIC",
                "PetLevel": "<90",
                "ProfitPercentage": "<30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "TFM",
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "IntroductionAgeDays": "20",
                "ForceBlacklist": "true",
                "FlipFinder": "FLIPPER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PRIVATE_ISLAND",
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ItemNameContains": "Portal to"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "FURNITURE",
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "TANK_MINER"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GOLD_PICKAXE",
            "displayName": "Golden Pickaxe",
            "filter": {
                "efficiency": "0-5",
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIAMOND_PICKAXE",
            "displayName": "Diamond Pickaxe",
            "filter": {
                "efficiency": "0-5",
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "RUNE"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "c248fe3bcbc740d795bb075b32acd70c",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "161695aa2c7d416894389fbb412ea51d",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "9a93d834e9594c34bf24f7826ba20ad7",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "87f2b48f5aeb4a79a486d77f53680157",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "7008b9b9829f42eb82f4a0630b4fd208",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "6bd68ee0d9604dc19ecb5a021ce75299",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "73ea22afd5774afba169d0f811f5a28f",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "8063c92570044d5b85e6387d43f2b74f",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "08375b58acea4ccf8daade140d8a300c",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "079cc71b41f64052bdd037bf8638f464",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "9f57ee301a82450da928f97cb2d1466c",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "12194ca938f749f089adbdd680ae6992",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "6dc845d12eab44218bd2a1bef466c920",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "d59c4ac3bd044430bd742897e8f8499a",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "31f5c1a45ff04903b2aa2a7518cb9011",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "5ce37b7b9d444460bc7eb08f4f814538",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Volatility": ">35",
                "ReferenceCount": "<10"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "efficiency": "6-6",
                "ReferenceCount": "<10",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<150"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WATER",
            "displayName": "Water (No Spread)",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STATIONARY_LAVA",
            "displayName": "Lava",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BAT_WAND",
            "displayName": "Spirit Sceptre",
            "filter": {
                "Stars": "6-10",
                "ProfitPercentage": "<20",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "FLIPPER",
                "ItemCategory": "PET",
                "ProfitPercentage": "<40",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "FLIPPER",
                "Volatility": ">30",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FLOWER_POT_ITEM",
            "displayName": "Flower Pot",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDER_DRAGON",
            "displayName": "Ender Dragon",
            "filter": {
                "PetSkin": "Any",
                "ForceBlacklist": "true",
                "FlipFinder": "STONKS"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "INFERNO_ROD",
            "displayName": "Inferno Rod",
            "filter": {
                "ProfitPercentage": "<30",
                "ForceBlacklist": "true",
                "Profit": "<30000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25",
                "FlipFinder": "FLIPPER",
                "ItemCategory": "COSMETIC"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25",
                "FlipFinder": "FLIPPER",
                "ItemCategory": "PET_SKIN"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JAR_OF_SAND",
            "displayName": "Jar of Sand",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": [
                "temp",
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JAR_OF_PICKLES",
            "displayName": "Jar of Pickles",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": [
                "temp",
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ZORROS_CAPE",
            "displayName": "Zorro's Cape",
            "filter": {
                "ProfitPercentage": "<50",
                "FlipFinder": "STONKS"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SPRING_BOOTS",
            "displayName": "Spring Boots",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<66"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MOLE",
            "displayName": "Mole",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<33",
                "PetItem": "PET_ITEM_QUICK_CLAW"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "FlipFinder": "STONKS",
                "Rarity": "RARE",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "FlipFinder": "STONKS",
                "Rarity": "UNCOMMON",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "FlipFinder": "STONKS",
                "Rarity": "COMMON",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ATTRIBUTE_SHARD",
            "displayName": "Attribute Shard",
            "filter": {
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_GAUNTLET",
            "displayName": "Gemstone Gauntlet",
            "filter": {
                "StartingBid": ">15000000",
                "ForceBlacklist": "true",
                "prismatic": "0-0"
            },
            "tags": [
                "anti manip",
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MOLDY_MUFFIN",
            "displayName": "Moldy Muffin",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "Skin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Skin": "SUPERIOR_SHIMMER",
                "FlipFinder": "CraftCost",
                "ProfitPercentage": "<66",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100",
                "ItemCategory": "HELMET"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PetSkin": "Any",
                "Volume": "<0.2",
                "ForceBlacklist": "true",
                "Profit": "<15000000",
                "ProfitPercentage": "<25"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "rune",
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GOLDEN_DANTE_STATUE",
            "displayName": "Golden Dante Statue",
            "filter": {
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ZORROS_CAPE",
            "displayName": "Zorro's Cape",
            "filter": {
                "ProfitPercentage": "<150",
                "ForceBlacklist": "true",
                "FlipFinder": "STONKS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BAL",
            "displayName": "Bal",
            "filter": {
                "PetSkin": "None",
                "ProfitPercentage": "<30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<80",
                "FlipFinder": "STONKS",
                "ItemNameContains": "omelette"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<80",
                "FlipFinder": "STONKS",
                "ItemNameContains": "upgrade module"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<80",
                "FlipFinder": "STONKS",
                "ItemNameContains": "fuel tank"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25",
                "FlipFinder": "STONKS",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BAL",
            "displayName": "Bal",
            "filter": {
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ARMADILLO",
            "displayName": "Armadillo",
            "filter": {
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GLACITE_GOLEM",
            "displayName": "Glacite Golem",
            "filter": {
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ICE_SPRAY_WAND",
            "displayName": "Ice Spray Wand",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25",
                "Rarity": "RARE"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_ICE_SPRAY_WAND",
            "displayName": "Ice Spray Wand",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25",
                "Rarity": "RARE"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NEW_YEAR_CAKE",
            "displayName": "New Year Cake",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<33"
            },
            "tags": [
                "anti manip"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "Rarity": "EPIC",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25",
                "PetSkin": "BABY_YETI_MIDNIGHT"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_HELMET",
            "displayName": "Fermento Helmet",
            "filter": {
                "Skin": "Any",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "GLOSSY_MINERAL",
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GLOSSY_MINERAL_TALISMAN",
            "displayName": "Glossy Mineral Talisman",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": null,
            "tags": [
                "customize"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FERMENTO",
                "Skin": "Any",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<25",
                "Profit": "<10m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<35",
                "FlipFinder": "STONKS",
                "Profit": "<20m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCARE_FRAGMENT",
            "displayName": "Scare Fragment",
            "filter": {
                "ProfitPercentage": "<150",
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GILLSPLASH_CLOAK",
            "displayName": "Gillsplash Cloak",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DITTO_SKULL",
            "displayName": "Ditto Skull",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BALLOON_SNAKE",
            "displayName": "Balloon Snake",
            "filter": {
                "Stars": "0-10",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_ULTIMATE",
            "displayName": "Harvester Helmet Skin",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "MINION_SKIN"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "THE_FISH"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Griffin Upgrade Stone",
                "Profit": "<20m",
                "ProfitPerUnit": "1m",
                "Recombobulated": "false"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "HOE",
                "ProfitPercentage": "<20",
                "Profit": "<10m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "New Year Cake"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Adaptive"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TRAINING_WEIGHTS",
            "displayName": "Training Weights",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DEFUSE_KIT",
            "displayName": "Defuse Kit",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "REAPER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "AXE",
                "ProfitPercentage": "<20",
                "Profit": "<5m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_GOGGLES",
            "displayName": "Shadow Goggles",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DARK_GOGGLES",
            "displayName": "Dark Goggles",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MENDER_HELMET",
            "displayName": "Mender Helmet",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MENDER_FEDORA",
            "displayName": "Mender Fedora",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "METAL_CHESTPLATE",
            "displayName": "Metal Chestplate",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STEEL_CHESTPLATE",
            "displayName": "Steel Chestplate",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STONE_CHESTPLATE",
            "displayName": "Stone Chestplate",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Katana",
                "Profit": "<100m",
                "Volume": "<0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "Rarity": "EPIC"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "FISHING_ROD"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_WITHER_SKELETON",
            "displayName": "Wither Skeleton",
            "filter": {
                "Rarity": "EPIC",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Perfect Chestplate",
                "ProfitPercentage": "<300",
                "Profit": "<100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Perfect Leggings",
                "ProfitPercentage": "<300"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Perfect Boots",
                "ProfitPercentage": "<300",
                "Profit": "<100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Perfect Helmet"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ISLAND_NPC",
            "displayName": "Island NPC",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "COCOA",
            "displayName": "Cocoa Plant",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TIC_TAC_TOE",
            "displayName": "Tic Tac Toe",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CONNECT_FOUR",
            "displayName": "Four in a Row",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARKOUR_CONTROLLER",
            "displayName": "Parkour Start/End",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARKOUR_POINT",
            "displayName": "Parkour Checkpoint",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARKOUR_TIMES",
            "displayName": "Parkour Times",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SOCIAL_DISPLAY",
            "displayName": "Social Display",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "EGG_HUNT",
            "displayName": "Egg Hunt",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ROCK_PAPER_SHEARS",
            "displayName": "Rock Paper Shears",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHOWCASE_BLOCK",
            "displayName": "Showcase Block",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "POISON_SAMPLE",
            "displayName": "Poison Sample",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BEACON",
            "displayName": "Beacon Block",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FRENCH_BREAD",
            "displayName": "French Bread",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PRECURSOR_EYE",
            "displayName": "Precursor Eye",
            "filter": {
                "ProfitPercentage": "<300"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Stars": "1-4",
                "FlipFinder": "STONKS",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_JELLYFISH",
            "displayName": "Jellyfish",
            "filter": {
                "Rarity": "EPIC",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<300"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLUE_WHALE",
            "displayName": "Blue Whale",
            "filter": {
                "CurrentEvent": "TravelingZoo",
                "ForceBlacklist": "true"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_LION",
            "displayName": "Lion",
            "filter": {
                "ForceBlacklist": "true",
                "CurrentEvent": "TravelingZoo"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_TIGER",
            "displayName": "Tiger",
            "filter": {
                "ForceBlacklist": "true",
                "CurrentEvent": "TravelingZoo"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GIRAFFE",
            "displayName": "Giraffe",
            "filter": {
                "ForceBlacklist": "true",
                "CurrentEvent": "TravelingZoo"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "ForceBlacklist": "true",
                "CurrentEvent": "TravelingZoo"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MONKEY",
            "displayName": "Monkey",
            "filter": {
                "ForceBlacklist": "true",
                "CurrentEvent": "TravelingZoo"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Horse Armor",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "MEMENTO"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMITE",
            "displayName": "Endermite",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MOOSHROOM_COW",
            "displayName": "Mooshroom Cow",
            "filter": {
                "PetItem": "MINOS_RELIC",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ENDER_PORTAL_FRAME",
            "displayName": "End Portal Frame",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SILEX",
            "displayName": "Sharp Rock",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PIGMAN_SWORD",
            "displayName": "Pigman Sword",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "TRAVEL_SCROLL",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "True",
                "ForceBlacklist": "true",
                "Profit": "<3000000",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": [
                "SmartFilters",
                "bed"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "True",
                "ForceBlacklist": "true",
                "Profit": "<2000000",
                "FlipFinder": "TFM"
            },
            "tags": [
                "SmartFilters",
                "bed"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "True",
                "ForceBlacklist": "true",
                "Profit": "<4000000",
                "FlipFinder": "STONKS"
            },
            "tags": [
                "SmartFilters",
                "bed"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "True",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<8",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": [
                "SmartFilters",
                "bed"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "True",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<15",
                "FlipFinder": "TFM"
            },
            "tags": [
                "SmartFilters",
                "bed"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "True",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<10",
                "FlipFinder": "STONKS"
            },
            "tags": [
                "SmartFilters",
                "bed"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "False",
                "Profit": "<3500000",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": [
                "SmartFilters",
                "nugget"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "False",
                "ForceBlacklist": "true",
                "Profit": "<4000000",
                "FlipFinder": "TFM"
            },
            "tags": [
                "SmartFilters",
                "nugget"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "False",
                "ForceBlacklist": "true",
                "Profit": "<5000000",
                "FlipFinder": "STONKS"
            },
            "tags": [
                "SmartFilters",
                "nugget"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "False",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<7",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": [
                "SmartFilters",
                "nugget"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "False",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<12",
                "FlipFinder": "STONKS"
            },
            "tags": [
                "SmartFilters",
                "nugget"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BedFlip": "False",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<20",
                "FlipFinder": "TFM"
            },
            "tags": [
                "SmartFilters",
                "nugget"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FLAMING_FIST",
            "displayName": "Flaming Fist",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HOLLOW_HELMET",
            "displayName": "Hollow Helmet",
            "filter": {
                "ProfitPercentage": "<50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VAMPIRE_WITCH_MASK",
            "displayName": "Vampire Witch Mask",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VAMPIRE_MASK",
            "displayName": "Vampire Mask",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_PHOENIX",
            "displayName": "Phoenix",
            "filter": {
                "Rarity": "EPIC",
                "ProfitPercentage": "<900"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FLEX_HELMET",
            "displayName": "Flex Helmet",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "BERSERKER",
                "ProfitPercentage": "<30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FINAL_DESTINATION",
                "Volume": "<1"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "CHEAP_TUXEDO"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_AMMONITE",
            "displayName": "Ammonite",
            "filter": {
                "CurrentMayor": "Marina"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_AMMONITE",
            "displayName": "Ammonite",
            "filter": {
                "LastMayor": "Marina"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_AMMONITE",
            "displayName": "Ammonite",
            "filter": {
                "NextMayor": "Marina"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SQUID",
            "displayName": "Squid",
            "filter": {
                "CurrentMayor": "Marina"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SQUID",
            "displayName": "Squid",
            "filter": {
                "LastMayor": "Marina"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SQUID",
            "displayName": "Squid",
            "filter": {
                "NextMayor": "Marina"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "NextMayor": "Marina"
            },
            "tags": [
                "event"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Backpack Skin"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REAPER_MASK",
            "displayName": "Reaper Mask",
            "filter": {
                "ProfitPercentage": "<50",
                "Volume": "2"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SNOWMAN",
            "displayName": "Snowman",
            "filter": {
                "ProfitPercentage": "<100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCOURGE_CLOAK",
            "displayName": "Scourge Cloak",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MEGALODON",
            "displayName": "Megalodon",
            "filter": {
                "LastMayor": "Marina"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BEE",
            "displayName": "Bee",
            "filter": {
                "PetLevel": "<100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARTY_CLOAK",
            "displayName": "Party Cloak",
            "filter": {
                "CurrentMayor": "Foxy"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARTY_CLOAK",
            "displayName": "Party Cloak",
            "filter": {
                "LastMayor": "Foxy"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ZORROS_CAPE",
            "displayName": "Zorro's Cape",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WITHER_GOGGLES",
            "displayName": "Wither Goggles",
            "filter": {
                "Reforge": "Wise",
                "ProfitPercentage": "<300"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GOLDEN_COLLAR",
            "displayName": "Golden Collar",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "ArmorDye",
                "ProfitPercentage": "<100",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ProfitPercentage": "<100",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ItemNameContains": "Dye",
                "Volume": "<48"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MINI_FISH_BOWL",
            "displayName": "Mini Fish Bowl",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SKELETON_MASTER_BOOTS",
            "displayName": "Skeleton Master Boots",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SKELETON_MASTER_HELMET",
            "displayName": "Skeleton Master Helmet",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SKELETON_MASTER_LEGGINGS",
            "displayName": "Skeleton Master Leggings",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_HOUND",
            "displayName": "Hound",
            "filter": {
                "PetLevel": "1-99",
                "Candy": "0-10",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RANCHERS_BOOTS",
            "displayName": "Rancher's Boots",
            "filter": {
                "ProfitPercentage": "<35",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCAVENGER_RING",
            "displayName": "Scavenger Ring",
            "filter": {
                "CurrentMayor": "Diaz",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCAVENGER_ARTIFACT",
            "displayName": "Scavenger Artifact",
            "filter": {
                "CurrentMayor": "Diaz",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCAVENGER_TALISMAN",
            "displayName": "Scavenger Talisman",
            "filter": {
                "CurrentMayor": "Diaz",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RING_OF_COINS",
            "displayName": "Ring of Coins",
            "filter": {
                "CurrentMayor": "Diaz",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RELIC_OF_COINS",
            "displayName": "Relic of Coins",
            "filter": {
                "CurrentMayor": "Diaz",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ARTIFACT_OF_COINS",
            "displayName": "Artifact of Coins",
            "filter": {
                "CurrentMayor": "Diaz",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CROWN_OF_AVARICE",
            "displayName": "Crown of Avarice",
            "filter": {
                "CurrentMayor": "Diaz"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HEARTMAW_DAGGER",
            "displayName": "Deathripper Dagger",
            "filter": {
                "Profit": "<50m",
                "Volume": "<2"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HEARTFIRE_DAGGER",
            "displayName": "Pyrochaos Dagger",
            "filter": {
                "Profit": "<50m",
                "Volume": "<2"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Abiphone",
                "ProfitPercentage": "<100",
                "Profit": "<100",
                "Volume": "<2"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Abicase",
                "ForceBlacklist": "true",
                "Profit": "<100m",
                "Volume": "<2"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHARK_SCALE_HELMET",
            "displayName": "Shark Scale Helmet",
            "filter": {
                "Skin": "None",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BONE_BOOMERANG",
            "displayName": "Bonemerang",
            "filter": {
                "ProfitPercentage": "<1000",
                "ForceBlacklist": "true"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "GLOSSY_MINERAL",
                "ProfitPercentage": "20",
                "Profit": "<100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_DRAGON",
            "displayName": "Aspect of the Dragons",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNIC_STAFF",
            "displayName": "Aurora Staff",
            "filter": {
                "ForceBlacklist": "true",
                "StartingBid": ">5m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "DIVAN",
                "FlipFinder": "STONKS",
                "NoOtherValuableEnchants": "true",
                "PerfectGemsCount": "0-0",
                "FlawlessGemsCount": "0-0",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "Profit": "<50m",
                "Volume": ">7"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "Profit": "<15m"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "Profit": "<50m",
                "hecatomb": "2-10"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "Volume": ">7",
                "ProfitPercentage": "<20"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "ItemNameContains": "Midas'"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "DyeItem": "Any",
                "ProfitPercentage": "<35",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GIANTS_SWORD",
            "displayName": "Giant's Sword",
            "filter": {
                "FlipFinder": "CraftCost",
                "StartingBid": ">500m",
                "Profit": "<250m",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ProfitPercentage": "<10",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost",
                "Volume": "<0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "BaseStatBoost": "1-50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "THUNDER",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<80"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemTier": "1-7"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "MAGMA_LORD"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "efficiency": "7-10",
                "Profit": "<30m",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "CurrentMayor": "Scorpius",
                "ForceBlacklist": "true",
                "StartingBid": ">100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "LastMayor": "Scorpius",
                "ForceBlacklist": "true",
                "StartingBid": ">100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "DIVER",
                "ForceBlacklist": "true",
                "Volume": "<4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost",
                "ProfitPercentage": "<7"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost",
                "Profit": "<5m"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "DyeItem": "Any",
                "Volume": "<1",
                "ForceBlacklist": "true",
                "Profit": "<100m",
                "ProfitPercentage": "<50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "CurrentMayor": "Scorpius",
                "ForceBlacklist": "true",
                "StartingBid": ">100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "ForceBlacklist": "true",
                "StartingBid": ">100m",
                "LastMayor": "Scorpius"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "MedianBased",
                "ItemCategory": "RUNE",
                "Profit": "<100m",
                "ProfitPercentage": "<100",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_SWORD",
            "displayName": "Midas' Sword",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MIDAS_SWORD",
            "displayName": "Midas' Sword",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STEAMED_CHOCOLATE_FISH",
            "displayName": "Fish Chocolat à la Vapeur",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SAND_CASTLE_BARN_SKIN",
            "displayName": "Sand Castle Barn Skin",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Griffin Upgrade Stone",
                "ActivePerk": "Mythological Ritual"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Griffin Upgrade Stone",
                "FlipFinder": "SNIPER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PerfectArmorTier": "1-13"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Volume": "<0.3",
                "Profit": "<30000000"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Volume": "<1",
                "ProfitPercentage": "<25",
                "Profit": "<20000000"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Volume": "<2",
                "ProfitPercentage": "<15",
                "Profit": "<6m"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Volume": "<1",
                "Profit": "<8m"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Volume": "<2",
                "Profit": "<6000000"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": "GAUNTLET_OF_CONTAGION",
            "displayName": "Gauntlet of Contagion",
            "filter": {},
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "OVERFLUX_CAPACITOR",
            "displayName": "Overflux Capacitor",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "OVERFLUX_POWER_ORB",
            "displayName": "Overflux Power Orb",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_TARANTULA_GREENBOTTLE",
            "displayName": "Greenbottle Tarantula Skin",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_DIAMOND_KNIGHT",
            "displayName": "Knight Skin",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<20",
                "Rarity": "EPIC",
                "PetSkin": "None",
                "Profit": "<50m",
                "Volume": "<6",
                "PetLevel": "2-99"
            },
            "tags": [
                "pet"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<20",
                "Rarity": "LEGENDARY",
                "PetSkin": "None",
                "Profit": "<50m",
                "Volume": "<6",
                "PetLevel": "2-99"
            },
            "tags": [
                "pet"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<20",
                "Rarity": "MYTHIC",
                "PetSkin": "None",
                "Profit": "<50m",
                "Volume": "<6",
                "PetLevel": "2-99"
            },
            "tags": [
                "pet"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<25",
                "Rarity": "MYTHIC",
                "PetSkin": "Any",
                "Profit": "<80m",
                "Volume": "<6",
                "PetLevel": "2-99"
            },
            "tags": [
                "pet"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<25",
                "Rarity": "LEGENDARY",
                "PetSkin": "Any",
                "Profit": "<80m",
                "Volume": "<6",
                "PetLevel": "2-99"
            },
            "tags": [
                "pet"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<25",
                "Rarity": "EPIC",
                "PetSkin": "Any",
                "Profit": "<80m",
                "Volume": "<6",
                "PetLevel": "2-99"
            },
            "tags": [
                "pet"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<20m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<20",
                "Volume": "<0.5"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<8m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<3.5"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<5m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<8"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<3m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<14"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": "CROWN_OF_AVARICE",
            "displayName": "Crown of Avarice",
            "filter": {
                "FlipFinder": "STONKS",
                "Profit": "<100m",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "Profit": "<100m",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<12m",
                "Volatility": ">12",
                "ForceBlacklist": "true"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Volume": "<20",
                "Profit": "<8m",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "CRYSTAL",
                "ForceBlacklist": "true"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volatility": ">40",
                "ForceBlacklist": "true",
                "FlipFinder": "FLIPPER"
            },
            "tags": [
                "Common Sense",
                "SmartFilters",
                "Should be part of cofl already wtf?"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volatility": ">74",
                "ForceBlacklist": "true",
                "Profit": "<100m",
                "ProfitPercentage": "<60"
            },
            "tags": [
                "Common Sense",
                "SmartFilters",
                "Should be part of cofl already wtf?"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<40m",
                "Volatility": ">9",
                "ProfitPercentage": "<27",
                "ForceBlacklist": "true",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [
                "SmartFilters",
                "Common Sense"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<60m",
                "Volatility": ">16",
                "ProfitPercentage": "<27",
                "ForceBlacklist": "true",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": [
                "SmartFilters",
                "Common Sense"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SCATHA_GOLDEN",
            "displayName": "Golden Scatha Skin",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "c3e1dd7a01d24ed3bb8b6c547036757c",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SNOWMAN_MASK",
            "displayName": "Snowman Mask",
            "filter": {
                "ForceBlacklist": "true",
                "CurrentEvent": "SeasonOfJerry"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "ELEGANT_TUXEDO",
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MENDER_CROWN",
            "displayName": "Mender Crown",
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Dye",
                "Volatility": ">40",
                "Profit": "<500m",
                "ProfitPercentage": "<100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Dye",
                "Profit": "<500m",
                "ProfitPercentage": "<100",
                "Rarity": "LEGENDARY"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Dye",
                "ProfitPercentage": "<50",
                "Rarity": "EPIC"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GILLSPLASH_BELT",
            "displayName": "Gillsplash Belt",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GILLSPLASH_GLOVES",
            "displayName": "Gillsplash Gloves",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "e65f2b7e920f4488aaf2176cdac4d929",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "e56a706f9c7349a8aa69c053db2a00c2",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ItemNameContains": "Lunar"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GOD_POTION_2",
            "displayName": "God Potion",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "GrandSearingRune": "3-3",
                "Stars": "6-10",
                "ProfitPercentage": "<50",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Mask",
                "ForceBlacklist": "true",
                "Profit": "<70m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ActivePerk": "A Time for Giving"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": ">7.233m",
                "ForceBlacklist": "true",
                "ProfitPercentage": ">92.32",
                "ConnectedMcUser": "580738bd72af4a0cbed78e25dada258e"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Skin",
                "Volatility": ">9",
                "Profit": "<100m",
                "ProfitPercentage": "<100",
                "ForceBlacklist": "true",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARTY_GLOVES",
            "displayName": "Party Gloves",
            "filter": {
                "ActivePerk": "A Time for Giving"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PARTY_BELT",
            "displayName": "Party Belt",
            "filter": {
                "ActivePerk": "A Time for Giving"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Dragon",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<200",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "DyeItem": "DYE_PURE_BLUE",
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost",
                "Profit": "<100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BLADE_OF_THE_VOLCANO",
            "displayName": "Blade of the Volcano",
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "GrandSearingRune": "1-3",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "GrandFreezingRune": "1-3",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {},
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {},
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {},
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {},
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HEGEMONY_ARTIFACT",
            "displayName": "Hegemony Artifact",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Seller": "f14c9e65c61d4095ad17b7b2d63f8485",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_WHALE_ORCA",
            "displayName": "OrcaBlue Whale",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCARF_GRIMOIRE",
            "displayName": "Scarf's Grimoire",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCARF_THESIS",
            "displayName": "Scarf's Thesis",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "EMBER_CHESTPLATE",
            "displayName": "Ember Chestplate",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Rarity": "COMMON"
            },
            "tags": [
                "Pets"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Rarity": "UNCOMMON"
            },
            "tags": [
                "Pets"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Rarity": "RARE"
            },
            "tags": [
                "Pets"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Rarity": "EPIC",
                "ProfitPercentage": "<30",
                "Volume": "<10"
            },
            "tags": [
                "Pets",
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Rarity": "LEGENDARY",
                "ProfitPercentage": "<30",
                "Volume": "<10",
                "PetLevel": "1-99"
            },
            "tags": [
                "Pets",
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Rarity": "LEGENDARY",
                "ProfitPercentage": "<25",
                "Volume": "<10",
                "PetLevel": "100"
            },
            "tags": [
                "Pets",
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Rarity": "MYTHIC",
                "ProfitPercentage": "<25",
                "Volume": "<10"
            },
            "tags": [
                "Pets",
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PIGGY_BANK",
            "displayName": "Piggy Bank",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BROKEN_PIGGY_BANK",
            "displayName": "Broken Piggy Bank",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TREASURE_TALISMAN",
            "displayName": "Treasure Talisman",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "RUNE",
                "ForceBlacklist": "true",
                "FlipFinder": "SNIPER_MEDIAN",
                "ProfitPercentage": "<200"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "dye",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<50",
                "Volume": "<10"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "ArmorDye",
                "ForceBlacklist": "true",
                "FlipFinder": "SNIPER_MEDIAN",
                "ProfitPercentage": "<100",
                "Volume": "<10"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Dagger",
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUMMONING_RING",
            "displayName": "Summoning Ring",
            "filter": {
                "Profit": "<7000000",
                "ProfitPercentage": "<75",
                "ForceBlacklist": "true"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "REAPER",
                "ProfitPercentage": "<75",
                "ForceBlacklist": "true"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FROZEN_BLAZE",
                "ProfitPercentage": "<50",
                "ForceBlacklist": "true"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_GAUNTLET",
            "displayName": "Gemstone Gauntlet",
            "filter": {
                "prismatic": "0-4",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<100"
            },
            "tags": [
                "Bad flips"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JUJU_SHORTBOW",
            "displayName": "Juju Shortbow",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUPERIOR_DRAGON_HELMET",
            "displayName": "Superior Dragon Helmet",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_DRILL",
            "displayName": "Divan's Drill",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_ALLOY",
            "displayName": "Divan's Alloy",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LOUDMOUTH_BASS",
            "displayName": "Loudmouth Bass",
            "filter": {
                "StartingBid": ">50m",
                "ProfitPercentage": "<1000",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "PetLevel": "2-99",
                "Candy": "1-10",
                "ForceBlacklist": "true",
                "Profit": "<10m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ATOMSPLIT_KATANA",
            "displayName": "Atomsplit Katana",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VORPAL_KATANA",
            "displayName": "Vorpal Katana",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VOIDEDGE_KATANA",
            "displayName": "Voidedge Katana",
            "filter": {},
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volatility": ">10",
                "ForceBlacklist": "true",
                "Profit": "<3m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volatility": ">25",
                "ForceBlacklist": "true",
                "Profit": "<10m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volatility": ">50",
                "ForceBlacklist": "true",
                "Profit": "<50m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "HOE",
                "Rarity": "RARE",
                "ProfitPercentage": "<45",
                "Volume": "<5",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Profit": ">5m",
                "FlipFinder": "STONKS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "STONKS",
                "ProfitPercentage": "<10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ProfitPercentage": "<35",
                "FlipFinder": "ALL_EXCEPT_USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<2.5m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<24"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<5m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CATACOMBS_EXPERT_RING",
            "displayName": "Catacombs Expert Ring",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WARDEN_HELMET",
            "displayName": "Warden Helmet",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "AhCategory": "WEAPON",
                "FlipFinder": "CraftCost",
                "Profit": "<20m",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNAANS_BOW",
            "displayName": "Runaan's Bow",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_BONE_BOOMERANG",
            "displayName": "Bonemerang",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_GAUNTLET",
            "displayName": "Gemstone Gauntlet",
            "filter": {
                "PerfectGemsCount": "0-0",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BALLOON_SNAKE",
            "displayName": "Balloon Snake",
            "filter": {
                "ProfitPercentage": "<33",
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FROZEN_BLAZE",
                "Profit": "<100m",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "POWER_WITHER",
                "Profit": "<100m",
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "TANK_WITHER",
                "Profit": "<100m",
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "SPEED_WITHER",
                "Profit": "<100m",
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "WISE_WITHER",
                "Profit": "<100m",
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "WITHER",
                "Profit": "<100m",
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JUJU_SHORTBOW",
            "displayName": "Juju Shortbow",
            "filter": {
                "ProfitPercentage": "<100",
                "Profit": "<100m",
                "ForceBlacklist": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_JERRY_SIGNATURE",
            "displayName": "Aspect of the Jerry, Signature Edition",
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_DRILL_4",
            "displayName": "Jasper Drill X",
            "filter": {
                "FlipFinder": "CraftCost",
                "ProfitPercentage": "<80",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ForceBlacklist": "true",
                "FlipFinder": "SNIPER",
                "Profit": "<20m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "HasAttribute": "true",
                "ForceBlacklist": "true",
                "Profit": "<40m",
                "FlipFinder": "SNIPER",
                "ProfitPercentage": "<69"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "HasAttribute": "true",
                "ForceBlacklist": "true",
                "Profit": "<100m",
                "FlipFinder": "SNIPER",
                "ProfitPercentage": "50",
                "StartingBid": ">150m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ProfitPercentage": "<7",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<20",
                "Profit": "<50m",
                "Volume": "<6",
                "PetLevel": "2-99",
                "AverageTimeToSell": ">4h"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "ProfitPercentage": "<25",
                "Profit": "<80m",
                "Volume": "<6",
                "AverageTimeToSell": ">4h"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Abiphone",
                "ProfitPercentage": "<33",
                "Profit": "<20m",
                "Volume": "<2",
                "AverageTimeToSell": ">12h"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JUNGLE_PICKAXE",
            "displayName": "Jungle Pickaxe",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_FUEL_TANK",
            "displayName": "Gemstone Fuel Tank",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ZORROS_CAPE",
            "displayName": "Zorro's Cape",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DEATH_BOW",
            "displayName": "Death Bow",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "DyeItem": "Any",
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PetSkin": "Any",
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FIGSTONE_AXE",
            "displayName": "Figstone Splitter",
            "filter": {
                "ForceBlacklist": "true",
                "ProfitPercentage": "<15"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_CHOCOLATE",
            "displayName": "Chocolate Dye",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Skin",
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_HANDLE",
            "displayName": "Necron's Handle",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": null,
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NETHERRACK_LOOKING_SUNSHADE",
            "displayName": "Netherrack-Looking Sunshade",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SALMON_HAT",
            "displayName": "Salmon Hat",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PetSkin": "Any",
                "ProfitPercentage": "<25",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PetSkin": "Any",
                "ProfitPercentage": "<35",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ProfitPercentage": "<35",
                "ForceBlacklist": "true",
                "ItemCategory": "PET_SKIN"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "STONKS",
                "Skin": "Any",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<40"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ItemCategory": "PET_SKIN",
                "Volatility": ">20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "ItemCategory": "PET_SKIN",
                "ProfitPercentage": "<40",
                "IntroductionAgeDays": "14"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "CRIMSON",
                "ForceBlacklist": "true",
                "ProfitPercentage": "<20"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCARF_STUDIES",
            "displayName": "Scarf's Studies",
            "filter": {
                "Recombobulated": "false",
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HIGH_CLASS_ARCHFIEND_DICE",
            "displayName": "High Class Archfiend Dice",
            "filter": {
                "ForceBlacklist": "true",
                "StartingBid": ">25m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Skin": "Any",
                "ForceBlacklist": "true",
                "FlipFinder": "USER"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DYE_FROG",
            "displayName": "Frog Dye",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CASTLE_BARN_SKIN",
            "displayName": "Castle Barn Skin",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVER_PUFFER",
            "displayName": "Puffer Fish Skin",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIGESTED_MOSQUITO",
            "displayName": "Digested Mosquito",
            "filter": {
                "StartingBid": ">1m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_ADAPTIVE_BELT",
            "displayName": "⚚ Adaptive Belt",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ADAPTIVE_BELT",
            "displayName": "Adaptive Belt",
            "filter": {
                "ForceBlacklist": "true"
            },
            "tags": [
                "temp"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<23.5m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.7",
                "AverageTimeToSell": ">33h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<500m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.027",
                "AverageTimeToSell": ">37d"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<1b",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.013",
                "AverageTimeToSell": ">77d"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<5b",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.002",
                "AverageTimeToSell": ">500d"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<10b",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.001",
                "AverageTimeToSell": ">1000d"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<187.76",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.071",
                "AverageTimeToSell": ">14d"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<50m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.268",
                "AverageTimeToSell": ">100h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<15.8m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<0.8",
                "AverageTimeToSell": ">19h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<10.6m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<1",
                "AverageTimeToSell": ">24h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<7.9m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<3",
                "AverageTimeToSell": ">8h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<5.5m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<4",
                "AverageTimeToSell": ">6h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<3.92m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<6",
                "AverageTimeToSell": ">3h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": "<2.6m",
                "FlipFinder": "ALL_EXCEPT_USER",
                "ForceBlacklist": "true",
                "Volume": "<8",
                "AverageTimeToSell": ">2h"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volume": "<0.3",
                "AverageTimeToSell": "<2d",
                "ForceBlacklist": "true",
                "Profit": "<40m"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": "HEARTMAW_DAGGER",
            "displayName": "Deathripper Dagger",
            "filter": {
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HEARTFIRE_DAGGER",
            "displayName": "Pyrochaos Dagger",
            "filter": {
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BURSTMAW_DAGGER",
            "displayName": "Mawdredge Dagger",
            "filter": {
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BURSTFIRE_DAGGER",
            "displayName": "Kindlebane Dagger",
            "filter": {
                "ProfitPercentage": "<20",
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FlipFinder": "CraftCost",
                "ForceBlacklist": "true",
                "PetItem": "PET_ITEM_TIER_BOOST"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Profit": "<20m",
                "AfterMainFilter": "true",
                "ItemNameContains": "Barn Skin"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ForceBlacklist": "true",
                "Profit": "<10m",
                "AfterMainFilter": "true",
                "DyeItem": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        }
    ],
    "whitelist": [
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CraftCostWeight": "default:0.9,pgems:1.0,drill_part_upgrade_module:1.0,drill_part_fuel_tank:1.0,drill_part_engine:1.0,dye_item:0.4",
                "ForTag": "DEFAULT",
                "DoNotRelist": "true"
            },
            "tags": [
                "Weight"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CraftCostWeight": "default:0.9,pgems:1.0,drill_part_upgrade_module:1.0,drill_part_fuel_tank:1.0,drill_part_engine:1.0,dye_item:0.09,growth.6:0,protection.6:0,hecatomb:0,big_brain:0.4",
                "ForTag": "DEFAULT"
            },
            "tags": [
                "Weight"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CraftCostWeight": "default:0.9,pgems:1.0,drill_part_upgrade_module:1.0,drill_part_fuel_tank:1.0,drill_part_engine:1.0,dye_item:0.2,growth.6:0,protection.6:0,growth.7:0.5,protection.7:0.5,hecatomb:0,big_brain:0.4,RUNE_GRAND_SEARING:0.2,RUNE_GRAND_FREEZING:0.1,upgrade_level:0.4,full_bid:0,power.7:0.6,rarity_upgrades:0.5",
                "ForTag": "Default",
                "DoNotRelist": "true"
            },
            "tags": [
                "Weight",
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CraftCostWeight": "default:0.8,pgems:1.0,drill_part_upgrade_module:1.0,drill_part_fuel_tank:1.0,drill_part_engine:1.0,dye_item:0.2,growth.6:0,protection.6:0,growth.7:0.5,protection.7:0.5,hecatomb:0,big_brain:0.4,RUNE_GRAND_SEARING:0.2,RUNE_GRAND_FREEZING:0.1,upgrade_level:0.4,full_bid:0,power.7:0.6,rarity_upgrades:0.5",
                "ForTag": "Default",
                "DoNotRelist": "true",
                "DrillPartEngine": "None",
                "DrillPartFuelTank": "None",
                "DrillPartUpgradeModule": "None",
                "efficiency": "0-7",
                "Profit": ">15m"
            },
            "tags": [
                "Weight",
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "Ethermerge": "yes",
                "MinProfit": "7000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "Ethermerge": "yes",
                "MinProfit": "7000000",
                "Recombobulated": "true"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WITHER_CLOAK",
            "displayName": "Wither Cloak Sword",
            "filter": {
                "ultimate_wise": "5-5",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": {
                "ultimate_chimera": "1-3",
                "ProfitPercentage": ">15"
            },
            "tags": [
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": {
                "ultimate_chimera": "1-3",
                "ProfitPercentage": ">15"
            },
            "tags": [
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "Rarity": "EPIC",
                "PetItem": "NOT_TIER_BOOST",
                "MaxCost": "100000000",
                "PetLevel": "1-50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "Rarity": "EPIC",
                "PetItem": "NOT_TIER_BOOST",
                "MaxCost": "140000000",
                "PetLevel": "100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Beastmaster Crest",
                "Rarity": "MYTHIC",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BURNING_KUUDRA_CORE",
            "displayName": "Burning Kuudra Core",
            "filter": {
                "MinProfit": "6000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FIERY_KUUDRA_CORE",
            "displayName": "Fiery Kuudra Core",
            "filter": {
                "MaxCost": "75000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BURSTSTOPPER_ARTIFACT",
            "displayName": "Burststopper Artifact",
            "filter": {
                "MaxCost": "75000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ENDER_RELIC",
            "displayName": "Ender Relic",
            "filter": {
                "MaxCost": "310000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ICE_SPRAY_WAND",
            "displayName": "Ice Spray Wand",
            "filter": {
                "ProfitPercentage": ">15",
                "Profit": ">6000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "WinningBid": ">100000000",
                "MinProfitPercentage": "30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DARK_CLAYMORE",
            "displayName": "Dark Claymore",
            "filter": {
                "MinProfitPercentage": "13"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GOLDEN_DRAGON",
            "displayName": "Golden Dragon",
            "filter": {
                "PetLevel": "200",
                "FlipFinder": "USER",
                "Candy": "0-0",
                "MaxCost": "700000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ARMOR_OF_MAGMA_CHESTPLATE",
            "displayName": "Armor of Magma Chestplate",
            "filter": {
                "ExoticColor": "Any:FFF9F0,E3E436,D8C3D7,D77F36,D55FDC,D0627A,C199B1,BD51D9,B24CD7,A14733,994DB6,8CB2E1,87CE26,81A9DA,7F3EB2,699BD8,5D4BB4,442849,32312F,312421,242522,24211F,20201F,1E1C19,1C1F2C,1B1A18,FFE4BF,E8E444,D78332,D3DCE1,B89E5F,B24B26,A54944,A49888,A14532,A04A43,A06FC9,9057BB,8C714C,784B28,654419,625442,628EA4,525252,3F399F,272625,241D19,DC8E4C,DBC86B,D98734,D78C30,CD7D49,8652A7,7FCC19,6D4478,6A99D4,6699D8,523712,515EB6,4F4F4E,342D19,1F241A,1C1C1C,1B1D1E,F5F8FB,D98239,9D3B40,976BC2,927752,8ED0D7,8743B3,8487AA,3F55AE,EBEAE2,E2E236,DC6FBB,A43D2D,9C3939,993536,924CCE,5662BA,332A21,332529,F8F8F7,6B94D8,6A9CD9,334CB2,222628,1F1F1F,D87F33,CC6319,BD4ED0,A53F2C,8BC71C,73A1D9,638AAD,232323,1F253F,FBFBFB,AD4628,352F25,262319,F9FAFB,F2BC19,CE9F97,9F392F,9A3636,993333,CC964C,191A1D,D2ACA4,FFF8EF,CC8B79,8C560C,7FCC18,E4E53F,626835,DC63D9,1C1A18,FFF3E2,442C3A,FFFFFF,FFFEFD,352815,FFFBF7,FFF1DF,EBC68E,1C1A1B,353433,FFC97F,322616,FFFDFB,272017,191918,1A1919,FEFEFE,201C18,1A1918,191919,FFFEFE",
                "FlipFinder": "USER",
                "MaxCost": "10000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ARMOR_OF_MAGMA_BOOTS",
            "displayName": "Armor of Magma Boots",
            "filter": {
                "ExoticColor": "Any:81A9DA,262323,251D2C,202020,1B1A1B,1A191B,FEFEFB,F7F7F7,EB96BA,D8A426,C36ED7,BD51D9,AD9FA5,9F374B,9C3939,927752,8C714C,7D464E,523712,4B4B4B,3F362A,344DB2,302A21,2D2038,20201F,1C1F2C,1C1D1E,191A1D,FFE4BF,FAFDF7,F17FA6,E5E539,E5E135,DC812D,D87F33,A470D7,9E70C3,81C61B,7ECB19,784B28,7195D1,5B73B7,4F63BB,251F17,1F1C1E,1C1D23,FCFCFD,F38BAB,ED79A3,ECD4F4,EBD026,C054D9,BF931A,BDCCE8,AA5233,9FA290,993333,839FDB,6D922C,6A4E31,5662BA,4255B4,231D24,1B1A18,FFFEFD,FFFDFB,FFF8EF,F4F7FB,EBC68E,9F392F,E4E433,E2E236,DFBBA9,BF8297,8542B3,473114,3D54B4,364EB2,1B1D1F,1A1918,D17733,8BC71C,7FCC19,5A6C30,334CB2,D98239,D0627A,B591BA,A53F2C,6998D4,342627,1E2124,1C1C1C,FFC97F,DC63D9,7F4C7F,322616,DFE8EF,B583AC,A43D2D,FDFDFD,CD7D49,6699D8,1A1A1A,F8EFE6,769DCE,524F4A,1B1919,F38192,C5928F,7FCC18,E5D20E,272017,FFFFFF,CC6319,EBEAE2,9CA5C2,32312F,352815,993332,88AEDC,FFF1DF,FEFEFE,686020,515EB6,FFFBF7,EB7975,AF4ED8,FFF3E2,D89D7D,191918,1F1C1D,1C1A18,CC964C,FFFEFE,D28A3F,29232D,8C560C,201C18,191919",
                "FlipFinder": "USER",
                "MaxCost": "8000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BAL",
            "displayName": "Bal",
            "filter": {
                "PetItem": "PET_ITEM_QUICK_CLAW",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TITANIUM_DRILL_1",
            "displayName": "Titanium Drill DR-X355",
            "filter": {
                "MinProfit": "6000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GIANTS_SWORD",
            "displayName": "Giant's Sword",
            "filter": {
                "MinProfitPercentage": "13",
                "PriorityOpen": "true",
                "MinProfit": "20000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REAPER_SWORD",
            "displayName": "Reaper Falchion",
            "filter": {
                "MinProfitPercentage": "20",
                "ultimate_one_for_all": "1-1"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SOUL_WHIP",
            "displayName": "Soul Whip",
            "filter": {
                "MinProfitPercentage": "30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THUNDER_IN_A_BOTTLE",
            "displayName": "Thunder in a Bottle",
            "filter": {
                "MinProfitPercentage": "333"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PANDORAS_BOX",
            "displayName": "Pandora's Box",
            "filter": {
                "MinProfitPercentage": "22"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WARDEN_HELMET",
            "displayName": "Warden Helmet",
            "filter": {
                "MinProfitPercentage": "12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REAPER_MASK",
            "displayName": "Reaper Mask",
            "filter": {
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_SHADOW_FURY",
            "displayName": "Shadow Fury",
            "filter": {
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "EmanKills": ">25000",
                "Recombobulated": "true",
                "MinProfitPercentage": "15",
                "ArmorSet": "FINAL_DESTINATION"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Abiphone",
                "MinProfitPercentage": "30",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CATACOMBS_EXPERT_RING",
            "displayName": "Catacombs Expert Ring",
            "filter": {
                "Recombobulated": "true",
                "MinProfit": "5000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CATACOMBS_EXPERT_RING",
            "displayName": "Catacombs Expert Ring",
            "filter": {
                "Recombobulated": "false",
                "MinProfit": "5000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "INFERNAL_KUUDRA_CORE",
            "displayName": "Infernal Kuudra Core",
            "filter": {
                "MaxCost": "325000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MASTER_SKULL_TIER_6",
            "displayName": "Master Skull - Tier 6",
            "filter": {
                "MaxCost": "80000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "Recombobulated": "true",
                "MinProfit": "5000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "Ethermerge": "yes",
                "ultimate_wise": "5-5",
                "MinProfit": "8000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "ultimate_wise": "5-5",
                "Recombobulated": "true",
                "MinProfit": "8000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "ultimate_wise": "5-5",
                "Recombobulated": "true",
                "Reforge": "warped_on_aote",
                "Ethermerge": "yes",
                "MinProfit": "10000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "ultimate_wise": "5-5",
                "Recombobulated": "true",
                "Reforge": "warped_on_aote",
                "MinProfit": "8000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "ultimate_wise": "5-5",
                "Reforge": "warped_on_aote",
                "MinProfit": "8000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "MinProfit": "4000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "MaxCost": "70000000",
                "DrillPartEngine": "sapphire_polished_drill_engine"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "MaxCost": "17000000",
                "DrillPartEngine": "titanium_drill_engine"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "MaxCost": "38000000",
                "DrillPartEngine": "ruby_polished_drill_engine"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "prismatic": "5-5",
                "MinProfit": "8000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "MaxCost": "24000000",
                "DrillPartFuelTank": "gemstone_fuel_tank"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "MaxCost": "45000000",
                "DrillPartFuelTank": "perfectly_cut_fuel_tank"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_BOOTS",
            "displayName": "Fermento Boots",
            "filter": {
                "Reforge": "mossy",
                "Recombobulated": "true",
                "MinProfit": "12000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RANCHERS_BOOTS",
            "displayName": "Rancher's Boots",
            "filter": {
                "Recombobulated": "true",
                "Reforge": "mossy",
                "MinProfit": "13000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RANCHERS_BOOTS",
            "displayName": "Rancher's Boots",
            "filter": {
                "Reforge": "mossy",
                "pesterminator": "5-5",
                "Recombobulated": "true",
                "MinProfit": "10000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RANCHERS_BOOTS",
            "displayName": "Rancher's Boots",
            "filter": {
                "Reforge": "mossy",
                "pesterminator": "5-5",
                "MinProfit": "10000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_LEGGINGS",
            "displayName": "Fermento Leggings",
            "filter": {
                "MinProfit": "7000000"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UNIQUE_RUNE_BARK_TUNES",
            "displayName": "◆ Bark Tunes Rune III",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_DRAGON",
            "displayName": "End Rune I",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_GRAND_SEARING",
            "displayName": "◆ Grand Searing Rune III",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_ENCHANT",
            "displayName": "Enchant Rune I",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "80-100",
                "MinProfitPercentage": "8",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "MinProfit": "40000000",
                "MinProfitPercentage": "11"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetLevel": "1-80",
                "MinProfitPercentage": "10",
                "MinProfit": "4500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WHEAT_2",
            "displayName": "Euclid's Wheat Hoe",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_1",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "7"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_3",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "7"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CARROT_2",
            "displayName": "Gauss Carrot Hoe",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CANE_1",
            "displayName": "Turing Sugar Cane Hoe",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CANE_3",
            "displayName": "Turing Sugar Cane Hoe",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MELON_DICER",
            "displayName": "Melon Dicer",
            "filter": {
                "MinProfit": "6500000",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER",
            "displayName": "Pumpkin Dicer",
            "filter": {
                "MinProfit": "4500000",
                "MinProfitPercentage": "12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER_3",
            "displayName": "Pumpkin Dicer 3.0",
            "filter": {
                "MinProfit": "4500000",
                "MinProfitPercentage": "12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RING_OF_COINS",
            "displayName": "Ring of Coins",
            "filter": {
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_DRAGON_PASTEL",
            "displayName": "PastelEnder Dragon",
            "filter": {
                "MinProfitPercentage": "25",
                "MinProfit": "10000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_DRAGON_NEON_RED",
            "displayName": "Neon RedEnder Dragon",
            "filter": {
                "MinProfitPercentage": "25",
                "MinProfit": "10000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WITHER_GOGGLES_CYBERPUNK",
            "displayName": "Cyberpunk Wither Goggles Skin",
            "filter": {
                "MinProfitPercentage": "25",
                "MinProfit": "10000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PIRATE_BOMB_FLUX",
            "displayName": "Pirate Bomb Power Orb Skin",
            "filter": {
                "MinProfitPercentage": "25",
                "MinProfit": "7500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SCATHA_ALBINO",
            "displayName": "Albino Scatha Skin",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_HOUND_BEAGLE",
            "displayName": "Beagle Hound Skin",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_MONKEY_GORILLA",
            "displayName": "GorillaMonkey",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_RABBIT_ROSE",
            "displayName": "RoseRabbit",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_CELESTIAL",
            "displayName": "Celestial Necron's Helmet Skin",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_ASSASSIN_ADMIRAL",
            "displayName": "Admiral Skin",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HEAT_CORE",
            "displayName": "Heat Core",
            "filter": {
                "MaxCost": "750000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "ultimate_soul_eater": "5-5",
                "power": "6-6",
                "overload": "5-5",
                "MaxCost": "400000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "ultimate_soul_eater": "5-5",
                "power": "7-7",
                "overload": "5-5",
                "MaxCost": "465000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": {
                "ultimate_chimera": "4-5",
                "ProfitPercentage": ">8"
            },
            "tags": [
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": {
                "ultimate_chimera": "4-5",
                "ProfitPercentage": ">8"
            },
            "tags": [
                "Adjust"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ENDER_ARTIFACT",
            "displayName": "Ender Artifact",
            "filter": {
                "MaxCost": "290000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHENS_REGALIA",
            "displayName": "Shen's Regalia",
            "filter": {
                "MaxCost": "335000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TREASURE_ARTIFACT",
            "displayName": "Treasure Artifact",
            "filter": {
                "MaxCost": "50000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GOD_POTION_2",
            "displayName": "God Potion",
            "filter": {
                "MaxCost": "500000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_PHOENIX",
            "displayName": "Phoenix",
            "filter": {
                "MinProfitPercentage": "50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "MinProfitPercentage": "50",
                "PetLevel": ">90"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DARK_CLAYMORE",
            "displayName": "Dark Claymore",
            "filter": {
                "Clean": "yes",
                "MinProfitPercentage": "10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RABBIT",
            "displayName": "Rabbit",
            "filter": {
                "Rarity": "LEGENDARY",
                "MinProfit": "3000000",
                "MinProfitPercentage": "10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "Rarity": "EPIC",
                "MinProfit": "4000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLAZE",
            "displayName": "Blaze",
            "filter": {
                "Rarity": "LEGENDARY",
                "MinProfit": "10000000",
                "MinProfitPercentage": "9"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLACK_CAT",
            "displayName": "Black Cat",
            "filter": {
                "Rarity": "LEGENDARY",
                "MinProfit": "10000000",
                "MinProfitPercentage": "10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FUNGI_CUTTER",
            "displayName": "Fungi Cutter",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMAN",
            "displayName": "Enderman",
            "filter": {
                "Rarity": "LEGENDARY",
                "MinProfitPercentage": "20",
                "MinProfit": "5000000",
                "PetLevel": "90-100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BAT_WAND",
            "displayName": "Spirit Sceptre",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "90-100",
                "MinProfit": "4000000",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_FURY",
            "displayName": "Shadow Fury",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "7500000",
                "Stars": "5-8"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WITHER_GOGGLES",
            "displayName": "Wither Goggles",
            "filter": {
                "MinProfitPercentage": "33",
                "MinProfit": "4000000",
                "Skin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "AXE_OF_THE_SHREDDED",
            "displayName": "Halberd of the Shredded",
            "filter": {
                "MinProfitPercentage": "10",
                "MinProfit": "10000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SOUL_WHIP",
            "displayName": "Soul Whip",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLAZE",
            "displayName": "Blaze",
            "filter": {
                "MinProfit": "5000000",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ADAPTIVE_BELT",
            "displayName": "Adaptive Belt",
            "filter": {
                "MinProfitPercentage": "50",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CARROT_1",
            "displayName": "Gauss Carrot Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CARROT_3",
            "displayName": "Gauss Carrot Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_POTATO_2",
            "displayName": "Pythagorean Potato Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CANE_1",
            "displayName": "Turing Sugar Cane Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CANE_3",
            "displayName": "Turing Sugar Cane Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WHEAT_2",
            "displayName": "Euclid's Wheat Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_1",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "MinProfitPercentage": "10",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_3",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "MinProfitPercentage": "7",
                "MinProfit": "3000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WHEAT_1",
            "displayName": "Euclid's Wheat Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "5000000",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CARROT_3",
            "displayName": "Gauss Carrot Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "5000000",
                "cultivating": "5-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CARROT_1",
            "displayName": "Gauss Carrot Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "5000000",
                "cultivating": "5-9"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_POTATO_3",
            "displayName": "Pythagorean Potato Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "5000000",
                "cultivating": "5-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_3",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "MinProfitPercentage": "10",
                "MinProfit": "5000000",
                "cultivating": "5-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_2",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "MinProfitPercentage": "10",
                "MinProfit": "5000000",
                "cultivating": "5-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CANE_3",
            "displayName": "Turing Sugar Cane Hoe",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "5000000",
                "cultivating": "5-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PRECURSOR_EYE",
            "displayName": "Precursor Eye",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER_2",
            "displayName": "Pumpkin Dicer 2.0",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "20",
                "cultivating": "1-4"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER",
            "displayName": "Pumpkin Dicer",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "5000000",
                "cultivating": "5-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER_3",
            "displayName": "Pumpkin Dicer 3.0",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "5000000",
                "cultivating": "5-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FLORID_ZOMBIE_SWORD",
            "displayName": "Florid Zombie Sword",
            "filter": {
                "ultimate_wise": "5-5",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DARK_CLAYMORE",
            "displayName": "Dark Claymore",
            "filter": {
                "MinProfit": "40000000",
                "MinProfitPercentage": "18"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_WITHER_SKELETON",
            "displayName": "Wither Skeleton",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "80-100",
                "MinProfitPercentage": "15",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JUJU_SHORTBOW",
            "displayName": "Juju Shortbow",
            "filter": {
                "Stars": "6-10",
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_TARANTULA",
            "displayName": "Tarantula",
            "filter": {
                "Rarity": "MYTHIC",
                "MinProfitPercentage": "20",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HIGH_CLASS_ARCHFIEND_DICE",
            "displayName": "High Class Archfiend Dice",
            "filter": {
                "MinProfitPercentage": "13",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WHEEL_OF_FATE",
            "displayName": "Wheel of Fate",
            "filter": {
                "MinProfitPercentage": "30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "KAT_BOUQUET",
            "displayName": "Kat Bouquet",
            "filter": {
                "MinProfit": "2000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "YETI_SWORD",
            "displayName": "Yeti Sword",
            "filter": {
                "MinProfit": "7500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SLUG",
            "displayName": "Slug",
            "filter": {
                "Rarity": "LEGENDARY",
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BEASTMASTER_CREST_LEGENDARY",
            "displayName": "Beastmaster Crest",
            "filter": {
                "Recombobulated": "false",
                "MinProfitPercentage": "15",
                "MinProfit": "3125000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "WinningBid": ">99999999",
                "MinProfitPercentage": "12",
                "MinProfit": "25000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_GAUNTLET",
            "displayName": "Gemstone Gauntlet",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LAVA_SHELL_NECKLACE",
            "displayName": "Lava Shell Necklace",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000",
                "lifeline": "1-5",
                "mana_pool": "1-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_CHESTPLATE",
            "displayName": "Fermento Chestplate",
            "filter": {
                "Recombobulated": "true",
                "Reforge": "mossy",
                "MinProfitPercentage": "8",
                "MinProfit": "7000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GYROKINETIC_WAND",
            "displayName": "Gyrokinetic Wand",
            "filter": {
                "MinProfitPercentage": "10",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ATMOSPHERIC_FILTER",
            "displayName": "Atmospheric Filter",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LOTUS_CLOAK",
            "displayName": "Lotus Cloak",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "3750000",
                "green_thumb": "1-3"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LOTUS_NECKLACE",
            "displayName": "Lotus Necklace",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "3750000",
                "green_thumb": "1-3"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LIVID_DAGGER",
            "displayName": "Livid Dagger",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DRACONIC_ARTIFACT",
            "displayName": "Draconic Artifact",
            "filter": {
                "MinProfitPercentage": "10",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_LION",
            "displayName": "Lion",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "80-100",
                "MinProfitPercentage": "15",
                "MinProfit": "4500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_LION",
            "displayName": "Lion",
            "filter": {
                "Rarity": "EPIC",
                "PetLevel": "90-100",
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GUARDIAN",
            "displayName": "Guardian",
            "filter": {
                "Rarity": "MYTHIC",
                "PetLevel": "90-100",
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "KAT_FLOWER",
            "displayName": "Kat Flower",
            "filter": {
                "MinProfit": "550000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_END",
            "displayName": "Aspect of the End",
            "filter": {
                "Reforge": "warped_on_aote",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNIC_STAFF",
            "displayName": "Aurora Staff",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMITE",
            "displayName": "Endermite",
            "filter": {
                "Rarity": "MYTHIC",
                "PetLevel": "90-100",
                "MinProfitPercentage": "33",
                "MinProfit": "7500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_2",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "MinProfitPercentage": "7",
                "MinProfit": "5000000",
                "cultivating": "0-0"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BEDROCK",
            "displayName": "Bedrock",
            "filter": {
                "MinProfit": "10000000",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ALPHA_PICK",
            "displayName": "Pioneer Pickaxe",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ARMADILLO",
            "displayName": "Armadillo",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "1-90",
                "MinProfitPercentage": "20",
                "MinProfit": "3500000",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FINAL_DESTINATION_BOOTS",
            "displayName": "Final Destination Boots",
            "filter": {
                "EmanKills": "25000-50000",
                "MinProfitPercentage": "6",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FINAL_DESTINATION_LEGGINGS",
            "displayName": "Final Destination Leggings",
            "filter": {
                "EmanKills": "25000-50000",
                "MinProfitPercentage": "17",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMAN",
            "displayName": "Enderman",
            "filter": {
                "Rarity": "EPIC",
                "PetLevel": "1-80",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_BOOTS",
            "displayName": "Fermento Boots",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_CHESTPLATE",
            "displayName": "Fermento Chestplate",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MITHRIL_GOLEM",
            "displayName": "Mithril Golem",
            "filter": {
                "PetLevel": "90-100",
                "Rarity": "LEGENDARY",
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER_2",
            "displayName": "Pumpkin Dicer 2.0",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "AUTO_RECOMBOBULATOR",
            "displayName": "Auto Recombobulator",
            "filter": {
                "MinProfitPercentage": "25",
                "MinProfit": "2500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_ASSASSIN_CLOAK",
            "displayName": "Shadow Assassin Cloak",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Abicase",
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FINAL_DESTINATION_BOOTS",
            "displayName": "Final Destination Boots",
            "filter": {
                "EmanKills": "2500-5000",
                "MinProfitPercentage": "300"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FINAL_DESTINATION_LEGGINGS",
            "displayName": "Final Destination Leggings",
            "filter": {
                "EmanKills": "2500-5000",
                "MinProfitPercentage": "300"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MINERAL_HELMET",
            "displayName": "Mineral Helmet",
            "filter": {
                "Reforge": "jaded",
                "MinProfitPercentage": "20",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MINERAL_CHESTPLATE",
            "displayName": "Mineral Chestplate",
            "filter": {
                "Reforge": "jaded",
                "MinProfitPercentage": "20",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_ARTIFACT",
            "displayName": "Fermento Artifact",
            "filter": {
                "MinProfitPercentage": "17",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_ROD",
            "displayName": "Magma Rod",
            "filter": {
                "trophy_hunter": "1-8",
                "double_hook": "1-7",
                "MinProfitPercentage": "15",
                "MinProfit": "400000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_DOLPHIN",
            "displayName": "Dolphin",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "1-90",
                "MinProfitPercentage": "15",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_TIGER",
            "displayName": "Tiger",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "1-80",
                "MinProfitPercentage": "20",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BEE",
            "displayName": "Bee",
            "filter": {
                "MinProfitPercentage": "50",
                "MinProfit": "4500000",
                "Rarity": "LEGENDARY"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_DRILL_2",
            "displayName": "Gemstone Drill LT-522",
            "filter": {
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FROZEN_SCYTHE",
            "displayName": "Frozen Scythe",
            "filter": {
                "ultimate_wise": "5-5",
                "MinProfitPercentage": "35",
                "MinProfit": "2500000",
                "Stars": "5-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SQUID",
            "displayName": "Squid",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "90-100",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GOLD_GIFT_TALISMAN",
            "displayName": "Gold Gift Talisman",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_PARROT",
            "displayName": "Parrot",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "100",
                "MinProfitPercentage": "15",
                "MinProfit": "6000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GRIFFIN_UPGRADE_STONE_LEGENDARY",
            "displayName": "Griffin Upgrade Stone",
            "filter": {
                "Recombobulated": "true",
                "MinProfit": "5000000",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PERFECT_BOOTS_13",
            "displayName": "Perfect Boots - Tier XIII",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PERFECT_CHESTPLATE_13",
            "displayName": "Perfect Chestplate - Tier XIII",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_WOLF",
            "displayName": "Wolf",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "1-90",
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BURSTFIRE_DAGGER",
            "displayName": "Kindlebane Dagger",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FINAL_DESTINATION_BOOTS",
            "displayName": "Final Destination Boots",
            "filter": {
                "EmanKills": ">50000",
                "MinProfitPercentage": "20",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FINAL_DESTINATION_LEGGINGS",
            "displayName": "Final Destination Leggings",
            "filter": {
                "EmanKills": ">50000",
                "MinProfitPercentage": "20",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_NECKLACE",
            "displayName": "Magma Necklace",
            "filter": {
                "veteran": "1-9",
                "vitality": "1-9",
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UPGRADE_STONE_GLACIAL",
            "displayName": "Wisp Upgrade Stone",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MAGMA_CUBE",
            "displayName": "Magma Cube",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "90-100",
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "Rarity": "RARE",
                "PetLevel": "100",
                "MinProfitPercentage": "25",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HEARTFIRE_DAGGER",
            "displayName": "Pyrochaos Dagger",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "30000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLUE_WHALE",
            "displayName": "Blue Whale",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "1-90",
                "MinProfitPercentage": "20",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLUE_WHALE",
            "displayName": "Blue Whale",
            "filter": {
                "Rarity": "EPIC",
                "PetLevel": "100",
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "STARRED_SHADOW_ASSASSIN",
                "MinProfitPercentage": "20",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_ASSASSIN_BOOTS",
            "displayName": "Shadow Assassin Boots",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_ASSASSIN_CHESTPLATE",
            "displayName": "Shadow Assassin Chestplate",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "CRIMSON",
                "MinProfitPercentage": "20",
                "MinProfit": "6000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_FURY",
            "displayName": "Shadow Fury",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "6000000",
                "Stars": "1-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_SHADOW_FURY",
            "displayName": "Shadow Fury",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "6000000",
                "Stars": "1-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIAMOND_PROFESSOR_HEAD",
            "displayName": "Diamond Professor Head",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "7500000",
                "Reforge": "ancient",
                "Stars": "5-10",
                "ultimate_legion": "3-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIAMOND_THORN_HEAD",
            "displayName": "Diamond Thorn Head",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "30000000",
                "Stars": "5-10",
                "Reforge": "ancient",
                "ultimate_legion": "3-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": true
        },
        {
            "tag": "VANQUISHED_GHAST_CLOAK",
            "displayName": "Vanquished Ghast Cloak",
            "filter": {
                "veteran": "1-8",
                "vitality": "1-7",
                "MinProfit": "7500000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NETHER_ARTIFACT",
            "displayName": "Nether Artifact",
            "filter": {
                "MinProfitPercentage": "9",
                "MinProfit": "6000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SNAIL",
            "displayName": "Snail",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "4000000",
                "PetLevel": "1-90"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "INFINI_VACUUM_HOOVERIUS",
            "displayName": "InfiniVacuum™ Hooverius",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MEGALODON",
            "displayName": "Megalodon",
            "filter": {
                "Rarity": "LEGENDARY",
                "PetLevel": "90-100",
                "MinProfitPercentage": "20",
                "MinProfit": "6000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "YOUNG_DRAGON",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF",
                "MaxCost": "15000000",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MONKEY",
            "displayName": "Monkey",
            "filter": {
                "MinProfitPercentage": "18",
                "MinProfit": "4000000",
                "Rarity": "LEGENDARY"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GLOWSTONE_GAUNTLET",
            "displayName": "Glowstone Gauntlet",
            "filter": {
                "mana_pool": "1-6",
                "mana_regeneration": "1-6",
                "MinProfitPercentage": "18",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<5000000",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GHAST_CLOAK",
            "displayName": "Ghast Cloak",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "60"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "MinProfitPercentage": "20",
                "MinProfit": "3000000",
                "Rarity": "EPIC",
                "PetLevel": "75-100"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BAL",
            "displayName": "Bal",
            "filter": {
                "Rarity": "EPIC",
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FROZEN_BLAZE_HELMET",
            "displayName": "Frozen Blaze Helmet",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "20",
                "Stars": "1-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FROZEN_BLAZE_LEGGINGS",
            "displayName": "Frozen Blaze Leggings",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "20",
                "Stars": "1-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUPERIOR_DRAGON_HELMET",
            "displayName": "Superior Dragon Helmet",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "20",
                "Skin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_DRILL_1",
            "displayName": "Ruby Drill TX-15",
            "filter": {
                "DrillPartEngine": "amber_polished_drill_engine",
                "DrillPartUpgradeModule": "goblin_omelette_blue_cheese",
                "DrillPartFuelTank": "perfectly_cut_fuel_tank",
                "DoNotRelist": "true",
                "MaxCost": "270000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERVOR_BOOTS",
            "displayName": "Fervor Boots",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "25"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERVOR_CHESTPLATE",
            "displayName": "Fervor Chestplate",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "25"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HOLLOW_LEGGINGS",
            "displayName": "Hollow Leggings",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECROMANCER_LORD_BOOTS",
            "displayName": "Necromancer Lord Boots",
            "filter": {
                "MinProfit": "3500000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECROMANCER_LORD_LEGGINGS",
            "displayName": "Necromancer Lord Leggings",
            "filter": {
                "MinProfit": "3500000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUPERIOR_DRAGON_HELMET",
            "displayName": "Superior Dragon Helmet",
            "filter": {
                "MinProfit": "100000000",
                "MinProfitPercentage": "15",
                "Skin": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERROR_LEGGINGS",
            "displayName": "Terror Leggings",
            "filter": {
                "MinProfit": "4000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CROWN_OF_AVARICE",
            "displayName": "Crown of Avarice",
            "filter": {
                "ultimate_legion": "5-5",
                "Recombobulated": "true",
                "MinProfitPercentage": "28"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WARDEN_HEART",
            "displayName": "Warden Heart",
            "filter": {
                "MaxCost": "85000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TITANIUM_DRILL_3",
            "displayName": "Titanium Drill DR-X555",
            "filter": {
                "MaxCost": "100000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "ultimate_soul_eater": "5-5",
                "overload": "5-5",
                "MaxCost": "400000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "overload": "5-5",
                "ultimate_duplex": "5-5",
                "MaxCost": "390000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "Rarity": "RARE",
                "MaxCost": "46000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PLASMA_NUCLEUS",
            "displayName": "Plasma Nucleus",
            "filter": {
                "FlipFinder": "USER",
                "MaxCost": "375000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PLASMA_NUCLEUS",
            "displayName": "Plasma Nucleus",
            "filter": {
                "FlipFinder": "USER",
                "MaxCost": "300000000",
                "CurrentMayor": "Scorpius",
                "LastMayor": "Scorpius",
                "NextMayor": "Scorpius"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GIANTS_SWORD",
            "displayName": "Giant's Sword",
            "filter": {
                "FlipFinder": "USER",
                "MaxCost": "70000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET_SKIN",
                "MinProfitPercentage": "50",
                "MinProfit": "5000000",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TARANTULA_CHESTPLATE",
            "displayName": "Tarantula Chestplate",
            "filter": {
                "FlipFinder": "USER",
                "MaxCost": "5000000",
                "ExoticColor": "Any:3C6746"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ARMOR_OF_MAGMA_LEGGINGS",
            "displayName": "Armor of Magma Leggings",
            "filter": {
                "ExoticColor": "Any:FFFEFF,1B1E2A,FEFEFD,FEFEF8,FCFEFB,F5ECE6,EE7FB1,EBC68E,E6DF2F,DCDB3C,D78332,D77E32,BFBBE8,B24CD8,AD7776,AB415B,A875DC,A06DC2,9A373A,993333,7949BE,785B4B,4B5282,32312F,24221F,21294A,1E1B1E,1B1B1A,191819,FCFDFB,F8F8F9,F88952,DC6FBB,CC964C,B35CBB,AC8DB8,A156C4,A14532,90A9C5,8C714C,8394C9,784B28,78483E,667F32,5A6B47,452B19,33479F,1C1C1C,1A191A,FEFDFD,F8F7F7,F38DAC,D88F55,D3885D,A86AD9,8CB2E1,674C32,484848,364EB2,352F25,272726,241F1A,1E2124,1C1A1D,1B1B1B,EB7975,D895AC,B75832,6699D8,353535,272727,1F1529,FFFEFD,FFE4BF,EBEAE2,EBD026,B84ED1,88AEDC,523712,332524,1B1919,1A1A1A,FBC4A8,D89D7D,D4D6DF,8BC71C,1A1A1D,1A1918,E2E236,C79F32,7683CF,6799D8,BF931A,A53F2C,A43D2D,8DCF17,7F3FB2,3A50B3,262626,1B1A18,D98239,FFFBF7,FBFBFB,F2BC19,F2809A,CC9591,A4A3A1,8C560C,626835,F59FBB,FFFFFF,FFF3E2,CC6319,D55FDC,927752,6E9ED8,D2ACA4,7FCC18,6F502F,E5D20E,A49888,352815,FFF9F0,D9E130,81A9DA,F0F0F0,272017,D87F33,FEFEFE,D8C3D7,FFF1DF,5D8AC1,B24ED8,334CB2,322616,FFC97F,191918,FFFEFE,1C1A18,201C18,FFF8EF,191919",
                "FlipFinder": "USER",
                "MaxCost": "10000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ARMOR_OF_MAGMA_HELMET",
            "displayName": "Armor of Magma Helmet",
            "filter": {
                "ExoticColor": "Any:30251C,2E2B26,272626,251D2C,232930,231F1C,1D1919,FDFCFD,FBFBFB,EBEFE5,C26CD8,B27E59,993434,8DCF17,8C714C,809DDB,7E3EB2,783DA3,737FC5,6A94D8,6672A5,5F8FD3,322616,302940,2C2521,2B3647,19181A,FFE4BF,FDFDF2,E58F82,E1A260,A14533,8CB2E1,88AEDC,805169,7986D7,784B28,6A8132,484848,251F17,202020,1C1F2C,19191B,FFF9F0,9F392F,9C3631,913131,793DA4,754288,6E98D2,6A4E31,688032,334CB2,24201C,211F21,FFFEFD,FFF3E2,F280A6,EB7975,D98239,D8AFAA,CCB18C,B25DD9,993632,8BC71C,6699D8,3349A8,1E1D19,1A191A,F5F4F2,F286A9,E5CAA5,D77E34,5F5D8C,4D4436,353433,2F281F,1B1E2A,1B1A18,1B1919,1A1A1D,FBC4A8,F2BC19,E3E235,E1B7DE,DC6FBB,D89D7D,201F1F,1C1C1C,D8DFCC,CC6319,A43D2D,495BA8,47532A,E4E3E2,A53F2C,993333,241F17,20201F,FEFDFD,CB948A,998D8C,334BB1,FFFFFF,DC63D9,C562DA,B04DD7,352F25,1C1A18,A54C4C,CC7E59,7FCC18,221D18,9CBADC,78BE20,352815,FFFBF7,433036,B2B2B2,7FCC19,32312F,8C560C,523712,D9853E,515EB6,FFFDFB,EBEAE2,E5E533,FFC97F,FFF8EF,FEFEFE,272017,FFFEFE,D87F33,1A1918,FFF1DF,191918,B49EC7,D87B4B,201C18,191919",
                "FlipFinder": "USER",
                "MaxCost": "10000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BAL",
            "displayName": "Bal",
            "filter": {
                "PetItem": "PET_ITEM_QUICK_CLAW",
                "FlipFinder": "USER",
                "MaxCost": "40000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DAEDALUS_AXE",
            "displayName": "Daedalus Blade",
            "filter": {
                "MinProfitPercentage": "25",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ATOMSPLIT_KATANA",
            "displayName": "Atomsplit Katana",
            "filter": {
                "Rarity": "LEGENDARY",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FIRE_FURY_STAFF",
            "displayName": "Fire Fury Staff",
            "filter": {
                "MinProfitPercentage": "50"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RADIOACTIVE_VIAL",
            "displayName": "Radioactive Vial",
            "filter": {
                "MinProfitPercentage": "30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SINSEEKER_SCYTHE",
            "displayName": "§4Sin§5seeker Scythe",
            "filter": {
                "MinProfitPercentage": "40"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TARANTULA_HELMET",
            "displayName": "Tarantula Helmet",
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PRECURSOR_EYE",
            "displayName": "Precursor Eye",
            "filter": {
                "MinProfitPercentage": "20",
                "Stars": "5-6"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ATOMSPLIT_KATANA",
            "displayName": "Atomsplit Katana",
            "filter": {
                "MinProfitPercentage": "30"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "FISHING_ROD",
                "MinProfitPercentage": "150",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PetSkin": "Any",
                "MinProfitPercentage": "30",
                "FlipFinder": "FLIPPER_AND_SNIPERS",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FROZEN_BLAZE",
                "MinProfitPercentage": "15",
                "FlipFinder": "FLIPPER_AND_SNIPERS",
                "MinProfit": "3500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_FURY",
            "displayName": "Shadow Fury",
            "filter": {
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "MinProfitPercentage": "200",
                "PetSkin": "SCATHA_GOLDEN",
                "FlipFinder": "STONKS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "ELEGANT_TUXEDO",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PIGMAN_SWORD",
            "displayName": "Pigman Sword",
            "filter": {
                "MinProfitPercentage": "20",
                "FlipFinder": "FLIPPER_AND_SNIPERS",
                "MinProfit": "3000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Rarity": "MYTHIC",
                "MinProfit": "5000000",
                "MinProfitPercentage": "20",
                "ArmorSet": "FINAL_DESTINATION"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MASTER_SKULL_TIER_7",
            "displayName": "Master Skull - Tier 7",
            "filter": {
                "MaxCost": "440000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "ultimate_wise": "5-5",
                "Reforge": "warped_on_aote",
                "Ethermerge": "yes",
                "MinProfit": "8000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_VOID",
            "displayName": "Aspect of the Void",
            "filter": {
                "Reforge": "warped_on_aote",
                "MinProfit": "7000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MITHRIL_DRILL_1",
            "displayName": "Mithril Drill SX-R226",
            "filter": {
                "compact": "1-10",
                "MinProfit": "5000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "GrandSearingRune": "3-3",
                "MaxCost": "550000000",
                "Stars": "10-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_BOOTS",
            "displayName": "Fermento Boots",
            "filter": {
                "MinProfit": "7000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_BOOTS",
            "displayName": "Fermento Boots",
            "filter": {
                "Recombobulated": "true",
                "MinProfit": "7000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_MUSIC",
            "displayName": "Music Rune I",
            "filter": {
                "MinProfit": "3500000",
                "ProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_MEOW_MUSIC",
            "displayName": "◆ Meow Music Rune III",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UNIQUE_RUNE",
            "displayName": "Rune",
            "filter": {
                "MinProfit": "3000000",
                "ProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_PRIMAL_FEAR",
            "displayName": "◆ Primal Fear Rune III",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UNIQUE_RUNE_ICE_SKATES",
            "displayName": "◆ Ice Skates Rune III",
            "filter": {
                "MinProfit": "3000000",
                "ProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UNIQUE_RUNE_GOLDEN_CARPET",
            "displayName": "◆ Golden Carpet Rune III",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_GOLDEN_CARPET",
            "displayName": "◆ Golden Carpet Rune III",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNE_FIERY_BURST",
            "displayName": "◆ Fiery Burst Rune I",
            "filter": {
                "MinProfit": "3000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUPERIOR_DRAGON_HELMET",
            "displayName": "Superior Dragon Helmet",
            "filter": {
                "DragonArmorSkin": "SUPERIOR_BABY",
                "StartingBid": "<2.35b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "OLD_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<5b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STRONG_DRAGON_HELMET",
            "displayName": "Strong Dragon Helmet",
            "filter": {
                "StartingBid": "<250m",
                "DoNotRelist": "true",
                "DragonArmorSkin": "STRONG_SHIMMER"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PROTECTOR_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<4.5b",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PROTECTOR_DRAGON_HELMET",
            "displayName": "Protector Dragon Helmet",
            "filter": {
                "Skin": "PROTECTOR_BABY",
                "MaxCost": "2000000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UNSTABLE_DRAGON_HELMET",
            "displayName": "Unstable Dragon Helmet",
            "filter": {
                "StartingBid": "<250m",
                "DoNotRelist": "true",
                "DragonArmorSkin": "UNSTABLE_SHIMMER"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_LIGHT_GREEN",
            "displayName": "Light GreenSheep",
            "filter": {
                "MaxCost": "10000000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_PINK",
            "displayName": "PinkSheep",
            "filter": {
                "MaxCost": "10000000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_LIGHT_BLUE",
            "displayName": "Light BlueSheep",
            "filter": {
                "MaxCost": "10000000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_PURPLE",
            "displayName": "PurpleSheep",
            "filter": {
                "MaxCost": "10000000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_WHITE",
            "displayName": "WhiteSheep",
            "filter": {
                "MaxCost": "10000000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SILVERFISH",
            "displayName": "Silverfish",
            "filter": {
                "PetSkin": "SILVERFISH_FOSSILIZED",
                "Candy": "0-0",
                "StartingBid": "<70m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SILVERFISH",
            "displayName": "Silverfish",
            "filter": {
                "PetSkin": "SILVERFISH_FOSSILIZED",
                "Candy": "1-10",
                "StartingBid": "<50m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_NEON_RED",
            "displayName": "Neon RedSheep",
            "filter": {
                "MaxCost": "5000000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_NEON_BLUE",
            "displayName": "Neon BlueSheep",
            "filter": {
                "MaxCost": "6500000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SENTINEL_WARDEN",
            "displayName": "Sentinel Warden Skin",
            "filter": {
                "MaxCost": "150000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RELIC_OF_COINS",
            "displayName": "Relic of Coins",
            "filter": {
                "MaxCost": "100000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GLOSSY_MINERAL_TALISMAN",
            "displayName": "Glossy Mineral Talisman",
            "filter": {
                "MinProfit": "6000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GLOSSY_MINERAL_TALISMAN",
            "displayName": "Glossy Mineral Talisman",
            "filter": {
                "MinProfit": "8000000",
                "Recombobulated": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Cat Food",
                "MaxCost": "80000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_HANDLE",
            "displayName": "Necron's Handle",
            "filter": {
                "StartingBid": "<472m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_HANDLE",
            "displayName": "Necron's Handle",
            "filter": {
                "StartingBid": "<545m",
                "DoNotRelist": "true",
                "IsShiny": "yes"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<540m",
                "AbilityScroll": "None",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<500m",
                "AbilityScroll": "None",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<505m",
                "AbilityScroll": "None",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<505m",
                "AbilityScroll": "None",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<500m",
                "AbilityScroll": "None",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<700m",
                "AbilityScroll": "None",
                "IsShiny": "yes",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<600m",
                "AbilityScroll": "None",
                "IsShiny": "yes",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<600m",
                "AbilityScroll": "None",
                "IsShiny": "yes",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<600m",
                "AbilityScroll": "None",
                "IsShiny": "yes",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<600m",
                "AbilityScroll": "None",
                "IsShiny": "yes",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "IMPLOSION_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "IMPLOSION_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "IMPLOSION_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "IMPLOSION_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<750m",
                "AbilityScroll": "IMPLOSION_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<750m",
                "AbilityScroll": "WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<850m",
                "AbilityScroll": "SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<750m",
                "AbilityScroll": "SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<1b",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "IMPLOSION_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<1b",
                "AbilityScroll": "IMPLOSION_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<950m",
                "AbilityScroll": "SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<1b",
                "AbilityScroll": "SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "StartingBid": "<1.42b",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "StartingBid": "<1.41b",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "StartingBid": "<1.42b",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "StartingBid": "<1.42b",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "NECRON_BLADE",
            "displayName": "Necron's Blade (Unrefined)",
            "filter": {
                "StartingBid": "<1.3b",
                "AbilityScroll": "IMPLOSION_SCROLL SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JUDGEMENT_CORE",
            "displayName": "Judgement Core",
            "filter": {
                "StartingBid": "<100m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "StartingBid": "<365m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_ALLOY",
            "displayName": "Divan's Alloy",
            "filter": {
                "StartingBid": "<775m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GOLDEN_DRAGON",
            "displayName": "Golden Dragon",
            "filter": {
                "PetLevel": "1-200",
                "StartingBid": "<500m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "StartingBid": "<50m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "StartingBid": "<250m",
                "DoNotRelist": "true",
                "PetItem": "NOT_TIER_BOOST",
                "Rarity": "LEGENDARY"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_BLACK",
            "displayName": "BlackSheep",
            "filter": {
                "StartingBid": "<50000000000",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "AfterMainFilter": "true",
                "Profit": ">5m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "MAGMA_LORD",
                "AfterMainFilter": "true",
                "FlipFinder": "ALL_EXCEPT_USER",
                "Profit": ">5m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PERFECT_CHESTPLATE_13",
            "displayName": "Perfect Chestplate - Tier XIII",
            "filter": {
                "AfterMainFilter": "true",
                "Profit": ">8m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PERFECT_BOOTS_13",
            "displayName": "Perfect Boots - Tier XIII",
            "filter": {
                "AfterMainFilter": "true",
                "Profit": ">8m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "ELEGANT_TUXEDO",
                "StartingBid": "<3m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Volume": ">20",
                "PetLevel": "1",
                "AfterMainFilter": "true",
                "Volatility": "<9"
            },
            "tags": [
                "Quick Pets"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "PET",
                "Volume": ">20",
                "PetLevel": "100",
                "AfterMainFilter": "true",
                "Volatility": "<9"
            },
            "tags": [
                "Quick Pets"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "POTATO_TALISMAN",
            "displayName": "Potato Talisman",
            "filter": {
                "ProfitPercentage": ">12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DWARVEN_METAL",
            "displayName": "Dwarven Metal Talisman",
            "filter": {
                "ProfitPercentage": ">12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CENTURY_TALISMAN",
            "displayName": "Talisman of the Century",
            "filter": {
                "ProfitPercentage": ">12"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DANTE_TALISMAN",
            "displayName": "Dante Talisman",
            "filter": {
                "Profit": ">2500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "ACCESSORY",
                "Rarity": "UNCOMMON",
                "ProfitPercentage": ">10",
                "Recombobulated": "true",
                "Profit": ">2500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "ACCESSORY",
                "Rarity": "RARE",
                "ProfitPercentage": ">10",
                "Recombobulated": "true",
                "Profit": ">2500000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<200m",
                "PetSkin": "ELEPHANT_RED",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<200m",
                "PetSkin": "ELEPHANT_GREEN",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<1.6b",
                "PetSkin": "ELEPHANT_BLUE",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HOLY_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<4.2b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WISE_DRAGON_HELMET",
            "displayName": "Wise Dragon Helmet",
            "filter": {
                "StartingBid": "<300m",
                "DoNotRelist": "true",
                "DragonArmorSkin": "WISE_SHIMMER"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MASTIFF_HELMET",
            "displayName": "Mastiff Crown",
            "filter": {
                "StartingBid": "<250m",
                "Skin": "MASTIFF_PUPPY",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "LegacyReforge": "True",
                "StartingBid": "<3m",
                "DoNotRelist": "true",
                "ItemCategory": "HELMET"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "LegacyReforge": "True",
                "StartingBid": "<3m",
                "DoNotRelist": "true",
                "ItemCategory": "SWORD"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "LegacyReforge": "True",
                "StartingBid": "<3m",
                "DoNotRelist": "true",
                "ItemCategory": "BOW"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "LegacyReforge": "True",
                "StartingBid": "<3m",
                "DoNotRelist": "true",
                "ItemCategory": "BOOTS"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "LegacyReforge": "True",
                "StartingBid": "<3m",
                "DoNotRelist": "true",
                "ItemCategory": "LEGGINGS"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "LegacyReforge": "True",
                "StartingBid": "<3m",
                "DoNotRelist": "true",
                "ItemCategory": "CHESTPLATE"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WARDEN_HELMET",
            "displayName": "Warden Helmet",
            "filter": {
                "Reforge": "Hurtful",
                "StartingBid": "<6b",
                "DoNotRelist": "true"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WARDEN_HELMET",
            "displayName": "Warden Helmet",
            "filter": {
                "Reforge": "Strong",
                "StartingBid": "<1b",
                "DoNotRelist": "true"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ZOMBIE_HEART",
            "displayName": "Zombie's Heart",
            "filter": {
                "Reforge": "Hurtful",
                "StartingBid": "<600m",
                "DoNotRelist": "true"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ZOMBIE_HEART",
            "displayName": "Zombie's Heart",
            "filter": {
                "Reforge": "Strong",
                "StartingBid": "<1b",
                "DoNotRelist": "true"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REVIVED_HEART",
            "displayName": "Revived Heart",
            "filter": {
                "Reforge": "Strong",
                "StartingBid": "<1b",
                "DoNotRelist": "true"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CRYSTALLIZED_HEART",
            "displayName": "Crystallized Heart",
            "filter": {
                "Reforge": "Strong",
                "StartingBid": "<1b",
                "DoNotRelist": "true"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASPECT_OF_THE_END",
            "displayName": "Aspect of the End",
            "filter": {
                "StartingBid": "<30m",
                "LegacyReforge": "True"
            },
            "tags": [
                "Legacy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "6699d8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure light blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "f27fa5",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure pink"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "e5e533",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure yellow"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "d87f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure orange"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "fc7f99",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure cyan"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "667f33",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure green"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7f3fb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure purple"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "B24cd8",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure magenta"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "334cb2",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure blue"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "993333",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure red"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "YOUNG_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "OLD_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "HOLY_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "WISE_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "STRONG_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<500m",
                "Color": "7fcc19",
                "DyeItem": "None",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON"
            },
            "tags": [
                "Exotic user pure lime"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "StartingBid": "<7m"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "WISE_WITHER",
                "StartingBid": "<175m",
                "DoNotRelist": "true",
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "WISE_WITHER",
                "StartingBid": "<175m",
                "DoNotRelist": "true",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "SPEED_WITHER",
                "StartingBid": "<165m",
                "DoNotRelist": "true",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "TANK_WITHER",
                "StartingBid": "<165m",
                "DoNotRelist": "true",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "POWER_WITHER",
                "StartingBid": "<175m",
                "DoNotRelist": "true",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "POWER_WITHER",
                "StartingBid": "<175m",
                "DoNotRelist": "true",
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "TANK_WITHER",
                "StartingBid": "<165m",
                "DoNotRelist": "true",
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "SPEED_WITHER",
                "StartingBid": "<165m",
                "DoNotRelist": "true",
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "OLD_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "PROTECTOR_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "WISE_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "OLD_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "HOLY_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "YOUNG_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "STRONG_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSetNoHelmet": "UNSTABLE_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None",
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "SUPERIOR_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "HOLY_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "YOUNG_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "WISE_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CrystalColor": "Crystal:1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "ArmorSetNoHelmet": "STRONG_DRAGON",
                "StartingBid": "<23m",
                "DyeItem": "None"
            },
            "tags": [
                "Crystal user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "FairyColor": "Fairy:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF",
                "StartingBid": "<7m"
            },
            "tags": [
                "Fairy user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "DRILL",
                "ProfitPercentage": ">13",
                "AfterMainFilter": "true",
                "Profit": ">25m",
                "FlipFinder": "CraftCost"
            },
            "tags": [
                "DEFAULT"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": ">50000000",
                "ProfitPercentage": ">20",
                "AfterMainFilter": "true",
                "FlipFinder": "CraftCost"
            },
            "tags": [
                "DEFAULT"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "AhCategory": "WEAPON",
                "FlipFinder": "CraftCost",
                "Profit": ">20m",
                "ProfitPercentage": ">20"
            },
            "tags": [
                "DEFAULT"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SPRING_BOOTS",
            "displayName": "Spring Boots",
            "filter": {
                "MinProfit": "30000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ICE_SPRAY_WAND",
            "displayName": "Ice Spray Wand",
            "filter": {
                "Profit": ">5m",
                "ProfitPercentage": ">10"
            },
            "tags": [
                "DEFAULT"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "EMBER_CHESTPLATE",
            "displayName": "Ember Chestplate",
            "filter": {
                "Profit": ">16m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "FINAL_DESTINATION",
                "ProfitPercentage": ">20",
                "Recombobulated": "true",
                "Reforge": "ancient",
                "Volume": ">2"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TREASURE_TALISMAN",
            "displayName": "Treasure Talisman",
            "filter": {
                "MinProfit": "3000000",
                "Recombobulated": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JUJU_SHORTBOW",
            "displayName": "Juju Shortbow",
            "filter": {
                "ProfitPercentage": ">15",
                "Recombobulated": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WARTS_3",
            "displayName": "Newton Nether Warts Hoe",
            "filter": {
                "Recombobulated": "true",
                "ProfitPercentage": ">8",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_WHEAT_3",
            "displayName": "Euclid's Wheat Hoe",
            "filter": {
                "ProfitPercentage": ">8",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MELON_DICER_3",
            "displayName": "Melon Dicer 3.0",
            "filter": {
                "ProfitPercentage": ">15",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PUMPKIN_DICER_3",
            "displayName": "Pumpkin Dicer 3.0",
            "filter": {
                "ProfitPercentage": ">15",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FUNGI_CUTTER",
            "displayName": "Fungi Cutter",
            "filter": {
                "ProfitPercentage": ">15",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_POTATO_3",
            "displayName": "Pythagorean Potato Hoe",
            "filter": {
                "ProfitPercentage": ">8",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CANE_3",
            "displayName": "Turing Sugar Cane Hoe",
            "filter": {
                "ProfitPercentage": ">8",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "THEORETICAL_HOE_CARROT_3",
            "displayName": "Gauss Carrot Hoe",
            "filter": {
                "ProfitPercentage": ">8",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MIDAS_SWORD",
            "displayName": "Midas' Sword",
            "filter": {
                "ProfitPercentage": ">15",
                "Volume": ">0.5",
                "FlipFinder": "MedianBased"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_SWORD",
            "displayName": "Midas' Sword",
            "filter": {
                "ProfitPercentage": ">15",
                "Volume": ">0.5",
                "FlipFinder": "MedianBased"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "ProfitPercentage": ">15",
                "Volume": ">0.5",
                "FlipFinder": "MedianBased"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STARRED_MIDAS_STAFF",
            "displayName": "Midas Staff",
            "filter": {
                "ProfitPercentage": ">15",
                "Volume": ">0.5",
                "FlipFinder": "MedianBased"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DCTR_SPACE_HELM",
            "displayName": "Space Helmet",
            "filter": {
                "StartingBid": "<3b",
                "DoNotRelist": "true",
                "RaffleYear": "Any"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "POTATO_BASKET",
            "displayName": "Basket of Hope from the Great Potato War",
            "filter": {
                "StartingBid": "<500m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GAME_BREAKER",
            "displayName": "Game Breaker",
            "filter": {
                "StartingBid": "<4b",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "BEDROCK",
            "displayName": "Bedrock",
            "filter": {
                "StartingBid": "<100m",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "CREATIVE_MIND",
            "displayName": "Creative Mind",
            "filter": {
                "StartingBid": "<4b",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "<50m",
                "Edition": "1-25"
            },
            "tags": [],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "RUNEBOOK",
            "displayName": "Runebook",
            "filter": {
                "StartingBid": "<500k"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ATOMSPLIT_KATANA",
            "displayName": "Atomsplit Katana",
            "filter": {
                "ProfitPercentage": ">15",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VORPAL_KATANA",
            "displayName": "Vorpal Katana",
            "filter": {
                "ProfitPercentage": ">15",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VOIDEDGE_KATANA",
            "displayName": "Voidedge Katana",
            "filter": {
                "ProfitPercentage": ">15",
                "Recombobulated": "true",
                "Volume": ">0.5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLUE_WHALE",
            "displayName": "Blue Whale",
            "filter": {
                "ProfitPercentage": ">8",
                "Rarity": "LEGENDARY",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "ProfitPercentage": ">8",
                "Rarity": "LEGENDARY",
                "Candy": "0-0",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "ProfitPercentage": ">15",
                "Rarity": "LEGENDARY",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_TIGER",
            "displayName": "Tiger",
            "filter": {
                "ProfitPercentage": ">15",
                "Rarity": "LEGENDARY",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SCATHA",
            "displayName": "Scatha",
            "filter": {
                "ProfitPercentage": ">15",
                "Rarity": "EPIC",
                "Candy": "0-0",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLACK_CAT",
            "displayName": "Black Cat",
            "filter": {
                "ProfitPercentage": ">8",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_JELLYFISH",
            "displayName": "Jellyfish",
            "filter": {
                "ProfitPercentage": ">15",
                "Candy": "0-0",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMAN",
            "displayName": "Enderman",
            "filter": {
                "ProfitPercentage": ">8",
                "Rarity": "MYTHIC",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MITHRIL_GOLEM",
            "displayName": "Mithril Golem",
            "filter": {
                "ProfitPercentage": ">15",
                "Rarity": "MYTHIC",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GRIFFIN",
            "displayName": "Griffin",
            "filter": {
                "ProfitPercentage": ">15",
                "Rarity": "LEGENDARY",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SQUID",
            "displayName": "Squid",
            "filter": {
                "ProfitPercentage": ">15",
                "Rarity": "LEGENDARY",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "PerfectGemsCount": "1-5",
                "ProfitPercentage": ">40",
                "FlipFinder": "CraftCost",
                "Profit": ">10m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "DrillPartEngine": "Any",
                "Profit": ">6m",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": ">6m",
                "FlipFinder": "CraftCost",
                "DrillPartFuelTank": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Profit": ">6m",
                "FlipFinder": "CraftCost",
                "DrillPartUpgradeModule": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volume": ">40",
                "AfterMainFilter": "true",
                "FlipFinder": "FLIPPER_AND_SNIPERS",
                "Volatility": "<9",
                "Profit": ">1.5m",
                "AverageTimeToSell": "<36m"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volume": ">80",
                "AfterMainFilter": "true",
                "FlipFinder": "FLIPPER_AND_SNIPERS",
                "Volatility": "<9",
                "Profit": ">500k",
                "AverageTimeToSell": "<18m"
            },
            "tags": [
                "SmartFilters"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JUNGLE_PICKAXE",
            "displayName": "Jungle Pickaxe",
            "filter": {
                "compact": "5-10",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GEMSTONE_FUEL_TANK",
            "displayName": "Gemstone Fuel Tank",
            "filter": {
                "MinProfit": "8000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MELON_DICER_3",
            "displayName": "Melon Dicer 3.0",
            "filter": {
                "Recombobulated": "false",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLAZE",
            "displayName": "Blaze",
            "filter": {
                "ProfitPercentage": ">15",
                "Rarity": "LEGENDARY",
                "Candy": "0-0",
                "PetLevel": "100",
                "PetSkin": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_LORD_HELMET",
            "displayName": "Magma Lord Helmet",
            "filter": {
                "Skin": "Any",
                "Recombobulated": "true",
                "ultimate_bobbin_time": "3-5",
                "MinProfit": "30000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_LORD_HELMET",
            "displayName": "Magma Lord Helmet",
            "filter": {
                "Skin": "Any",
                "Recombobulated": "true",
                "MinProfit": "25000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "INFERNO_ROD",
            "displayName": "Inferno Rod",
            "filter": {
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_ROD",
            "displayName": "Magma Rod",
            "filter": {
                "double_hook": "4-10",
                "MinProfit": "1500000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_ROD",
            "displayName": "Magma Rod",
            "filter": {
                "double_hook": "6-10",
                "MinProfit": "1500000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_ROD",
            "displayName": "Magma Rod",
            "filter": {
                "double_hook": "7-10",
                "MinProfit": "3000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_ROD",
            "displayName": "Magma Rod",
            "filter": {
                "double_hook": "8-10",
                "MinProfit": "5000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MAGMA_ROD",
            "displayName": "Magma Rod",
            "filter": {
                "double_hook": "9-10",
                "MinProfit": "8000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ENDER_DRAGON_BABY_BLUE",
            "displayName": "Baby Blue Ender Dragon Skin",
            "filter": {
                "MinProfitPercentage": "25",
                "MinProfit": "8000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_DOLPHIN_SNUBNOSE_PURPLE",
            "displayName": "Purple Snubfin Dolphin Skin",
            "filter": {
                "MinProfit": "35000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_MONKEY_LEMUR",
            "displayName": "Lemur Monkey Skin",
            "filter": {
                "MinProfitPercentage": "45",
                "MinProfit": "20000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_AMMONITE_MAGMA",
            "displayName": "Magma Ammonite Skin",
            "filter": {
                "MinProfit": "15000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SCATHA_DARK",
            "displayName": "Dark Scatha Skin",
            "filter": {
                "MinProfit": "15000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "LUNAR_RABBIT_HAT",
            "displayName": "Lunar Rabbit Hat Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_GRIFFIN_REINDRAKE",
            "displayName": "Reindrake Griffin Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_TIGER_NEON",
            "displayName": "Neon Tiger Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_MOOSHROOM_COW_MOOCELIUM",
            "displayName": "Moocelium Mooshroom Cow Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_JELLYFISH_LUMINESCENT",
            "displayName": "Luminescent Jellyfish Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ENDER_DRAGON_UNDEAD",
            "displayName": "Undead Ender Dragon Skin",
            "filter": {
                "MinProfit": "15000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_BAL_INFERNO",
            "displayName": "Inferno Bal Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ENDERMAN_NEON",
            "displayName": "Neon Enderman Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_JERRY_HANDSOME",
            "displayName": "Handsome Jerry Skin",
            "filter": {
                "MinProfit": "15000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_DOLPHIN_SNUBNOSE_GREEN",
            "displayName": "Green Snubfin Dolphin Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_DOLPHIN_SNUBFIN",
            "displayName": "Snubfin Dolphin Skin",
            "filter": {
                "MinProfit": "15000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_OCELOT_SNOW_TIGER",
            "displayName": "Snow Tiger Ocelot Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_BAT_VAMPIRE",
            "displayName": "Vampire Bat Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_TIGER_SABER_TOOTH",
            "displayName": "Saber-Tooth Tiger Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FERMENTO_BLOOM",
            "displayName": "Bloom Skin",
            "filter": {
                "MinProfit": "15000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SHADOW_ASSASSIN_SLY_FOX",
            "displayName": "Sly Fox Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FROZEN_BLAZE_ICICLE",
            "displayName": "Icicle Skin",
            "filter": {
                "MinProfit": "40000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "JERRY_BARN_SKIN",
            "displayName": "Jerry Barn Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "GLOWING_GRAPE_FLUX",
            "displayName": "Glowing Grape Power Orb Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "FROG_BARN_SKIN",
            "displayName": "Frog Barn Skin",
            "filter": {
                "MinProfit": "30000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MANA_FLUX_POWER_ORB",
            "displayName": "Mana Flux Power Orb",
            "filter": {
                "Skin": "Any",
                "MinProfitPercentage": "28",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HELLFIRE_ROD",
            "displayName": "Hellfire Rod",
            "filter": {
                "trophy_hunter": "1-8",
                "double_hook": "1-7",
                "MinProfit": "12000000",
                "MinProfitPercentage": "20"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "INFERNO_ROD",
            "displayName": "Inferno Rod",
            "filter": {
                "MinProfit": "25000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WISE_WITHER_HELMET",
            "displayName": "Storm's Helmet",
            "filter": {
                "growth": "7-7",
                "big_brain": "5-5",
                "Skin": "STORM_CELESTIAL",
                "Stars": "10-10",
                "Reforge": "ancient",
                "MinProfit": "100000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WISE_WITHER_HELMET",
            "displayName": "Storm's Helmet",
            "filter": {
                "Skin": "GOLDOR_CELESTIAL",
                "Stars": "9-10",
                "Reforge": "ancient",
                "MinProfit": "100000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WISE_WITHER_HELMET",
            "displayName": "Storm's Helmet",
            "filter": {
                "growth": "7-7",
                "big_brain": "5-5",
                "Skin": "STORM_CELESTIAL",
                "Stars": "10-10",
                "IsShiny": "yes",
                "Reforge": "ancient",
                "MinProfit": "100000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WISE_WITHER_HELMET",
            "displayName": "Storm's Helmet",
            "filter": {
                "Skin": "NECRON_CELESTIAL",
                "Stars": "10-10",
                "Reforge": "ancient",
                "MinProfit": "100000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ENDERMAN",
            "displayName": "SpookyEnderman",
            "filter": {
                "StartingBid": "<2.7b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RABBIT",
            "displayName": "Rabbit",
            "filter": {
                "PetSkin": "RABBIT",
                "Candy": "0-0",
                "Profit": "200m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RABBIT",
            "displayName": "Rabbit",
            "filter": {
                "PetSkin": "RABBIT_LUNAR",
                "Candy": "0-0",
                "MinProfit": "100000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVER_PUFFER",
            "displayName": "Puffer Fish Skin",
            "filter": {
                "StartingBid": "<400m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REAPER_SPIRIT",
            "displayName": "Spirit Skin",
            "filter": {
                "StartingBid": "<2b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REAPER_MASK",
            "displayName": "Reaper Mask",
            "filter": {
                "Skin": "REAPER_SPIRIT",
                "StartingBid": "<1.5b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ROCK_THINKING",
            "displayName": "ThinkingRock",
            "filter": {
                "StartingBid": "<18.250b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_THINKING",
                "Candy": "0-0",
                "StartingBid": "<1.5b"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_LAUGH",
                "Candy": "0-0",
                "StartingBid": "<10b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_LAUGH",
                "Candy": "1-10",
                "StartingBid": "<1.5b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ROCK_SMILE",
            "displayName": "SmilingRock",
            "filter": {
                "StartingBid": "<10b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_SMILE",
                "Candy": "0-0",
                "StartingBid": "<3b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_SMILE",
                "Candy": "1-10",
                "StartingBid": "<200m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ROCK_EMBARRASSED",
            "displayName": "EmbarrassedRock",
            "filter": {
                "StartingBid": "<20b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_EMBARRASSED",
                "Candy": "0-0",
                "StartingBid": "<13b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_EMBARRASSED",
                "Candy": "1-10",
                "StartingBid": "<2b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ROCK_DERP",
            "displayName": "DerpyRock",
            "filter": {
                "StartingBid": "<20b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "Candy": "0-0",
                "PetSkin": "ROCK_DERP",
                "StartingBid": "<10b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "Candy": "1-10",
                "PetSkin": "ROCK_DERP",
                "StartingBid": "<1.8b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ROCK_COOL",
            "displayName": "CoolRock",
            "filter": {
                "StartingBid": "<10b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_COOL",
                "Candy": "0-0",
                "StartingBid": "<3.2b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ROCK",
            "displayName": "Rock",
            "filter": {
                "PetSkin": "ROCK_COOL",
                "Candy": "1-10",
                "StartingBid": "<900m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "MASTIFF_PUPPY",
            "displayName": "Puppy Skin",
            "filter": {
                "StartingBid": "<725m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PERFECT_FORGE",
            "displayName": "Reinforced Skin",
            "filter": {
                "MinProfit": "75000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TARANTULA_BLACK_WIDOW",
            "displayName": "Black Widow Skin",
            "filter": {
                "MinProfit": "100000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "OLD_DRAGON_HELMET",
            "displayName": "Old Dragon Helmet",
            "filter": {
                "StartingBid": "<1.5b",
                "DoNotRelist": "true",
                "DragonArmorSkin": "OLD_BABY"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "OLD_DRAGON_HELMET",
            "displayName": "Old Dragon Helmet",
            "filter": {
                "StartingBid": "<150m",
                "DoNotRelist": "true",
                "DragonArmorSkin": "OLD_SHIMMER"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STRONG_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<3.3b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "STRONG_DRAGON_HELMET",
            "displayName": "Strong Dragon Helmet",
            "filter": {
                "StartingBid": "<2.3b",
                "DoNotRelist": "true",
                "DragonArmorSkin": "STRONG_BABY"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HOLY_DRAGON_HELMET",
            "displayName": "Holy Dragon Helmet",
            "filter": {
                "StartingBid": "<550m",
                "DoNotRelist": "true",
                "DragonArmorSkin": "HOLY_BABY"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HOLY_DRAGON_HELMET",
            "displayName": "Holy Dragon Helmet",
            "filter": {
                "StartingBid": "<350m",
                "DoNotRelist": "true",
                "DragonArmorSkin": "HOLY_SHIMMER"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "YOUNG_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<7b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WISE_DRAGON_HELMET",
            "displayName": "Wise Dragon Helmet",
            "filter": {
                "StartingBid": "<3.4b",
                "DoNotRelist": "true",
                "DragonArmorSkin": "WISE_BABY"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "YOUNG_DRAGON_HELMET",
            "displayName": "Young Dragon Helmet",
            "filter": {
                "StartingBid": "<3b",
                "DoNotRelist": "true",
                "DragonArmorSkin": "YOUNG_BABY"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UNSTABLE_DRAGON_HELMET",
            "displayName": "Unstable Dragon Helmet",
            "filter": {
                "StartingBid": "<3.3b",
                "DoNotRelist": "true",
                "DragonArmorSkin": "UNSTABLE_BABY"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_WITHER",
            "displayName": "DarkWither Skeleton",
            "filter": {
                "StartingBid": "<1b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_WITHER_SKELETON",
            "displayName": "Wither Skeleton",
            "filter": {
                "PetSkin": "WITHER",
                "StartingBid": "<450m",
                "Candy": "0-0",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_WITHER_SKELETON",
            "displayName": "Wither Skeleton",
            "filter": {
                "PetSkin": "WITHER",
                "StartingBid": "<400m",
                "Candy": "1-10",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SILVERFISH",
            "displayName": "FortifiedSilverfish",
            "filter": {
                "StartingBid": "<400m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SILVERFISH",
            "displayName": "Silverfish",
            "filter": {
                "PetSkin": "SILVERFISH",
                "Candy": "0-0",
                "StartingBid": "<80m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SILVERFISH",
            "displayName": "Silverfish",
            "filter": {
                "PetSkin": "SILVERFISH",
                "Candy": "1-10",
                "StartingBid": "<50m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_YETI_GROWN_UP",
            "displayName": "Grown-upBaby Yeti",
            "filter": {
                "StartingBid": "<1b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetSkin": "YETI_GROWN_UP",
                "Candy": "0-0",
                "MinProfit": "45000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetSkin": "YETI_GROWN_UP",
                "Candy": "1-10",
                "MinProfit": "30000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetSkin": "BABY_YETI_MIDNIGHT",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetSkin": "BABY_YETI_LIGHT_SASQUATCH",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetSkin": "BABY_YETI_MIDNIGHT",
                "PetLevel": "100",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetSkin": "BABY_YETI_PLUSHIE",
                "PetLevel": "100",
                "Candy": "0-0",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BABY_YETI",
            "displayName": "Baby Yeti",
            "filter": {
                "PetSkin": "BABY_YETI_PLUSHIE",
                "PetLevel": "100",
                "MinProfit": "15000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_JERRY_RED_ELF",
            "displayName": "Red ElfJerry",
            "filter": {
                "StartingBid": "<4.8b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_JERRY_GREEN_ELF",
            "displayName": "Green ElfJerry",
            "filter": {
                "StartingBid": "<3.2b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_MONKEY_GOLDEN",
            "displayName": "GoldenMonkey",
            "filter": {
                "StartingBid": "<850m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MONKEY",
            "displayName": "Monkey",
            "filter": {
                "PetSkin": "MONKEY_GOLDEN",
                "Candy": "0-0",
                "MinProfit": "100000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MONKEY",
            "displayName": "Monkey",
            "filter": {
                "PetSkin": "MONKEY_GOLDEN",
                "Candy": "1-10",
                "MinProfit": "55000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_MONKEY",
            "displayName": "Monkey",
            "filter": {
                "PetSkin": "MONKEY_GORILLA",
                "Candy": "0-0",
                "StartingBid": "<135m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SILVERFISH_FOSSILIZED",
            "displayName": "FossilizedSilverfish",
            "filter": {
                "StartingBid": "<150m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLUE_WHALE",
            "displayName": "Blue Whale",
            "filter": {
                "Candy": "0-0",
                "PetSkin": "WHALE_ORCA",
                "StartingBid": "<135m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_BLUE_WHALE",
            "displayName": "Blue Whale",
            "filter": {
                "Candy": "1-10",
                "PetSkin": "WHALE_ORCA",
                "StartingBid": "<130m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WITHER_GOGGLES_CYBERPUNK",
            "displayName": "Cyberpunk Wither Goggles Skin",
            "filter": {
                "MaxCost": "350000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WITHER_GOGGLES",
            "displayName": "Wither Goggles",
            "filter": {
                "Skin": "WITHER_GOGGLES_CYBERPUNK",
                "MaxCost": "225000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_SHEEP_BLACK_WOOLY",
            "displayName": "Black Wooly Sheep Skin",
            "filter": {
                "MinProfit": "25000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ENDER_DRAGON_BABY",
            "displayName": "Baby Ender Dragon Skin",
            "filter": {
                "MinProfit": "8000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TRUE_WARDEN",
            "displayName": "True Warden Skin",
            "filter": {
                "StartingBid": "<900m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ELEPHANT_GREEN",
            "displayName": "GreenElephant",
            "filter": {
                "StartingBid": "<1.5b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ELEPHANT_PURPLE",
            "displayName": "PurpleElephant",
            "filter": {
                "StartingBid": "<950m"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ELEPHANT_BLUE",
            "displayName": "BlueElephant",
            "filter": {
                "StartingBid": "<8b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ELEPHANT_ORANGE",
            "displayName": "OrangeElephant",
            "filter": {
                "StartingBid": "<4.7b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ELEPHANT_RED",
            "displayName": "RedElephant",
            "filter": {
                "StartingBid": "<1b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ELEPHANT_PINK",
            "displayName": "PinkElephant",
            "filter": {
                "StartingBid": "<9.3b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<300m",
                "PetSkin": "ELEPHANT_PURPLE",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<350m",
                "PetSkin": "ELEPHANT_RED",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<500m",
                "PetSkin": "ELEPHANT_GREEN",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<100m",
                "PetSkin": "ELEPHANT_PURPLE",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<2b",
                "PetSkin": "ELEPHANT_ORANGE",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<2b",
                "PetSkin": "ELEPHANT_BLUE",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<500m",
                "PetSkin": "ELEPHANT_ORANGE",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<2b",
                "PetSkin": "ELEPHANT_PINK",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ELEPHANT",
            "displayName": "Elephant",
            "filter": {
                "StartingBid": "<1.5b",
                "PetSkin": "ELEPHANT_PINK",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<4b",
                "PetSkin": "SHEEP_NEON_BLUE",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<800m",
                "PetSkin": "SHEEP_NEON_BLUE",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<20b",
                "PetSkin": "SHEEP_PINK",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<2b",
                "PetSkin": "SHEEP_PINK",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<30b",
                "PetSkin": "SHEEP_LIGHT_GREEN",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<5b",
                "PetSkin": "SHEEP_LIGHT_GREEN",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<19b",
                "PetSkin": "SHEEP_LIGHT_BLUE",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<5b",
                "PetSkin": "SHEEP_LIGHT_BLUE",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<30b",
                "PetSkin": "SHEEP_WHITE",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<8b",
                "PetSkin": "SHEEP_WHITE",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<20b",
                "PetSkin": "SHEEP_PURPLE",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SHEEP",
            "displayName": "Sheep",
            "filter": {
                "StartingBid": "<500m",
                "PetSkin": "SHEEP_PURPLE",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_RAT_LUNAR",
            "displayName": "Lunar Rat Skin",
            "filter": {
                "StartingBid": "<1.5b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_RABBIT_LUNAR_BABY",
            "displayName": "Lunar Baby Rabbit Skin",
            "filter": {
                "StartingBid": "<800m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_DOLPHIN_LUNAR_HORSE",
            "displayName": "Lunar Sea Horse Dolphin Skin",
            "filter": {
                "StartingBid": "<1.7b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ENDER_DRAGON_LUNAR",
            "displayName": "Lunar Ender Dragon Skin",
            "filter": {
                "StartingBid": "<100m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_PIGMAN_LUNAR_PIG",
            "displayName": "Lunar Pig Pigman Skin",
            "filter": {
                "StartingBid": "<2.1b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_TIGER_LUNAR",
            "displayName": "Lunar Tiger Skin",
            "filter": {
                "StartingBid": "<200m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_MONKEY_LUNAR",
            "displayName": "Lunar Monkey Skin",
            "filter": {
                "StartingBid": "<425m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_CHICKEN_LUNAR_ROOSTER",
            "displayName": "Lunar Rooster Chicken Skin",
            "filter": {
                "StartingBid": "<800m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_HOUND_LUNAR",
            "displayName": "Lunar Dog Hound Skin",
            "filter": {
                "StartingBid": "<550m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_RABBIT",
            "displayName": "PrettyRabbit",
            "filter": {
                "StartingBid": "<6.7b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RABBIT",
            "displayName": "Rabbit",
            "filter": {
                "PetSkin": "RABBIT",
                "Candy": "0-0",
                "MinProfit": "100000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RABBIT",
            "displayName": "Rabbit",
            "filter": {
                "PetSkin": "RABBIT",
                "Candy": "1-10",
                "MinProfit": "100000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RABBIT",
            "displayName": "Rabbit",
            "filter": {
                "PetSkin": "RABBIT_LUNAR_BABY",
                "Candy": "0-0",
                "MinProfit": "100000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_RABBIT",
            "displayName": "Rabbit",
            "filter": {
                "PetSkin": "RABBIT_LUNAR_BABY",
                "Candy": "1-10",
                "MinProfit": "80000000"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GUARDIAN",
            "displayName": "Guardian",
            "filter": {
                "StartingBid": "<2b",
                "PetSkin": "GUARDIAN",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_GUARDIAN",
            "displayName": "Guardian",
            "filter": {
                "StartingBid": "<200m",
                "PetSkin": "GUARDIAN",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_GUARDIAN",
            "displayName": "WatcherGuardian",
            "filter": {
                "StartingBid": "<3.4b"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_TIGER",
            "displayName": "Tiger",
            "filter": {
                "StartingBid": "<2.6b",
                "PetSkin": "TIGER_TWILIGHT",
                "Candy": "0-0"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_TIGER",
            "displayName": "Tiger",
            "filter": {
                "StartingBid": "<1.6b",
                "PetSkin": "TIGER_TWILIGHT",
                "Candy": "1-10"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_TIGER_TWILIGHT",
            "displayName": "TwilightTiger",
            "filter": {
                "StartingBid": "<7b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUPERIOR_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<3b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "UNSTABLE_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<8b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "WISE_BABY",
            "displayName": "Baby Skin",
            "filter": {
                "StartingBid": "<7b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "YOUNG_DRAGON_HELMET",
            "displayName": "Young Dragon Helmet",
            "filter": {
                "StartingBid": "<350m",
                "DragonArmorSkin": "YOUNG_SHIMMER",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMAN",
            "displayName": "Enderman",
            "filter": {
                "PetSkin": "ENDERMAN",
                "StartingBid": "<1.2b",
                "Candy": "0-0",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDERMAN",
            "displayName": "Enderman",
            "filter": {
                "PetSkin": "ENDERMAN",
                "StartingBid": "<700m",
                "Candy": "1-10",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_SKIN_ROCK_LAUGH",
            "displayName": "LaughingRock",
            "filter": {
                "StartingBid": "<15b",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SUPERIOR_DRAGON_HELMET",
            "displayName": "Superior Dragon Helmet",
            "filter": {
                "DragonArmorSkin": "SUPERIOR_SHIMMER",
                "StartingBid": "<500m",
                "DoNotRelist": "true"
            },
            "tags": [
                "skin user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "REAPER_RED_ONI_MASK",
            "displayName": "Red Oni Reaper Mask Skin",
            "filter": {
                "MinProfit": "20000000",
                "MinProfitPercentage": "45"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DEATH_BOW",
            "displayName": "Death Bow",
            "filter": {
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "GILLSPLASH",
                "MinProfit": "7000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "DRILL",
                "FlipFinder": "CraftCost"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "0-50m",
                "MinProfit": "15000000",
                "ItemNameContains": "Dye"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "50m-100m",
                "MinProfit": "25000000",
                "ItemNameContains": "Dye"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "100m-200m",
                "MinProfit": "45000000",
                "ItemNameContains": "Dye"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "200m-300m",
                "MinProfit": "75000000",
                "ItemNameContains": "Dye"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "300m-400m",
                "ItemNameContains": "Dye",
                "Profit": ">100m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "StartingBid": "500-750m",
                "ItemNameContains": "Dye",
                "Profit": ">150m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TITANIUM_DRILL_2",
            "displayName": "Titanium Drill DR-X455",
            "filter": {
                "MinProfit": "8000000",
                "DrillPartEngine": "None",
                "DrillPartFuelTank": "None",
                "DrillPartUpgradeModule": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "MinProfit": "50000000",
                "Stars": "6-7"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "MinProfit": "65000000",
                "Stars": "7-9"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TERMINATOR",
            "displayName": "Terminator",
            "filter": {
                "MinProfit": "100000000",
                "Stars": "10-10"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "SORROW",
                "Recombobulated": "true",
                "MinProfit": "5000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "SORROW",
                "Recombobulated": "true",
                "MinProfit": "8000000",
                "UnlockedSlots": "5-5",
                "PerfectGemsCount": "0-0"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ArmorSet": "SORROW",
                "Recombobulated": "true",
                "MinProfit": "10000000",
                "UnlockedSlots": "5-5",
                "PerfectGemsCount": "5-5",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "OVERFLUX_CAPACITOR",
            "displayName": "Overflux Capacitor",
            "filter": {
                "MinProfit": "12000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "OVERFLUX_POWER_ORB",
            "displayName": "Overflux Power Orb",
            "filter": {
                "MinProfit": "12000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "DyeItem": "Any",
                "MinProfit": "30000000",
                "Stars": "0-5"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Recombobulated": "true",
                "ItemCategory": "ACCESSORY",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "ACCESSORY",
                "TalismanEnrichment": "Any",
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "Volume": ">2",
                "ProfitPercentage": ">160",
                "Profit": ">3m",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "EstProfitPerHour": ">10m",
                "Profit": ">1.5m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "EstProfitPerHour": ">30",
                "ProfitPercentage": ">50",
                "StartingBid": ">1.5m",
                "Profit": "2m",
                "FlipFinder": "FLIPPER_AND_SNIPERS"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "15",
                "MinProfit": "6000000",
                "CraftCostWeight": "Growth:0.4,protection:0.4,default:0.3",
                "ArmorSet": "POWER_WITHER"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "13",
                "MinProfit": "22000000",
                "CraftCostWeight": "growth:0.4,protection:0.4,default:0.3",
                "ArmorSet": "POWER_WITHER"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "13",
                "MinProfit": "22000000",
                "CraftCostWeight": "dye_item.dye_pure_white:0.55,dye_item:0.1,default:0.3",
                "ArmorSet": "POWER_WITHER"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "13",
                "MinProfit": "22000000",
                "CraftCostWeight": "dye_item.dye_pure_black:0.6,dye_item:0.1,default:0.3",
                "ArmorSet": "POWER_WITHER"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "13",
                "MinProfit": "22000000",
                "CraftCostWeight": "dye_item.DYE_WARDEN:0.6,dye_item:0.1,default:0.3",
                "ArmorSet": "POWER_WITHER"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "13",
                "MinProfit": "22000000",
                "CraftCostWeight": "dye_item.DYE_WARDEN:0.4,dye_item:0.1,default:0.3",
                "ArmorSet": "POWER_WITHER"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "13",
                "MinProfit": "22000000",
                "CraftCostWeight": "dye_item.dye_pure_black:0.5,dye_item:0.1,default:0.3",
                "ArmorSet": "WISE_WITHER"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CraftCostWeight": "default:0.75,rarity_upgrades:0.05,hotpc:0.2,unlocked_slots:0.35,upgrade_level:0.4,skin:0.35,dye_item:0.05,gilded:0.2,festive:0.2,fabled:0.2,rooted:0.1,giant:0.2,jaded:0.3,auspicious:0.2,submerged:0.2,mossy:0.3,renowned:0.3,champion:0.2,prosecute:0.5,cleave:0.1,ender_slayer:0.2,strong_mana:0.3,green_thumb:0.25,pristine:0.2,compact:0.2,growth:0.1,protection:0.1,giant_killer:0.2,overload:0.2,hardened_mana:0.2,cultivating:0.2,smarty_pants:0.2,smite:0.2,hecatomb:0.1,dragon_hunter:0.2,efficiency:0.5,ultimate_soul_eater:0.2,ultimate_combo:0.2,ultimate_wise:0.6,ultimate_last_stand:0.3,ultimate_legion:0.5,ultimate_one_for_all:0,ultimate_wisdom:0.5,rune_grand_searing:0.3,winning_bid:0.1,full_bid:0.1,art_of_war_count:0.2,artofpeaceapplied:0.05,farming_for_dummies_count:0.3,divan_powder_coating:0.5,drill_part_fuel_tank:0.85,drill_part_upgrade_module:0.85,drill_part_engine:0.85,pgems:0.85",
                "MinProfit": "7000000",
                "MinProfitPercentage": "15"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CraftCostWeight": "default:0.75,rarity_upgrades:0.05,hotpc:0.2,unlocked_slots:0.35,upgrade_level:0.4,skin:0.35,dye_item:0.05,gilded:0.2,festive:0.2,fabled:0.2,rooted:0.1,giant:0.2,jaded:0.3,auspicious:0.2,submerged:0.2,mossy:0.3,renowned:0.3,champion:0.2,prosecute:0.5,cleave:0.1,ender_slayer:0.2,strong_mana:0.3,green_thumb:0.25,pristine:0.2,compact:0.2,growth:0.1,protection:0.1,giant_killer:0.2,overload:0.2,hardened_mana:0.2,cultivating:0.2,smarty_pants:0.2,smite:0.2,hecatomb:0.1,dragon_hunter:0.2,efficiency:0.5,ultimate_soul_eater:0.2,ultimate_combo:0.2,ultimate_wise:0.6,ultimate_last_stand:0.3,ultimate_legion:0.5,ultimate_one_for_all:0,ultimate_wisdom:0.5,rune_grand_searing:0.3,winning_bid:0.1,full_bid:0.1,art_of_war_count:0.2,artofpeaceapplied:0.05,farming_for_dummies_count:0.3,divan_powder_coating:0.5,drill_part_fuel_tank:0.85,drill_part_upgrade_module:0.85,drill_part_engine:0.85,pgems:0.85",
                "MinProfit": "7000000",
                "MinProfitPercentage": "16"
            },
            "tags": [
                "Craftcost"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ExoticColor": "Fairy+Crystal:330066,4C0099,660033,660066,6600CC,7F00FF,99004C,990099,9933FF,B266FF,CC0066,CC00CC,CC99FF,E5CCFF,FF007F,FF00FF,FF3399,FF33FF,FF66B2,FF66FF,FF99CC,FF99FF,FFCCE5,FFCCFF,1F0030,46085E,54146E,5D1C78,63237D,6A2C82,7E4196,8E51A6,9C64B3,A875BD,B88BC9,C6A3D4,D9C1E3,E5D1ED,EFE1F5,FCF3FF",
                "MaxCost": "12000000",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_DRILL",
            "displayName": "Divan's Drill",
            "filter": {
                "ProfitPercentage": "8"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "MinProfit": "65000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "SCYLLA",
            "displayName": "Scylla",
            "filter": {
                "MinProfit": "75000000",
                "AbilityScroll": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "MinProfit": "65000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "VALKYRIE",
            "displayName": "Valkyrie",
            "filter": {
                "MinProfit": "75000000",
                "AbilityScroll": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "MinProfit": "65000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "HYPERION",
            "displayName": "Hyperion",
            "filter": {
                "MinProfit": "75000000",
                "AbilityScroll": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "MinProfit": "65000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ASTRAEA",
            "displayName": "Astraea",
            "filter": {
                "MinProfit": "75000000",
                "AbilityScroll": "Any"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "TITANIUM_DRILL_ENGINE",
            "displayName": "Titanium-Plated Drill Engine",
            "filter": {
                "MinProfit": "4000000"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_HELMET",
            "displayName": "Helmet of Divan",
            "filter": {
                "UnlockedSlots": "5-5",
                "PerfectGemsCount": "5-5",
                "StartingBid": "<70m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_CHESTPLATE",
            "displayName": "Chestplate of Divan",
            "filter": {
                "UnlockedSlots": "5-5",
                "PerfectGemsCount": "5-5",
                "StartingBid": "<70m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_LEGGINGS",
            "displayName": "Leggings of Divan",
            "filter": {
                "UnlockedSlots": "5-5",
                "PerfectGemsCount": "5-5",
                "StartingBid": "<70m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_BOOTS",
            "displayName": "Boots of Divan",
            "filter": {
                "UnlockedSlots": "5-5",
                "PerfectGemsCount": "5-5",
                "StartingBid": "<70m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "DUNGEON_ITEM",
                "MinProfitPercentage": "15",
                "MinProfit": "8000000",
                "CraftCostWeight": "Growth:0.05,protection:0.05,default:0.3"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "jasper0Gem": "PERFECT",
                "MaxCost": "5000000",
                "DoNotRelist": "true"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MaxCost": "5000000",
                "jasper1Gem": "PERFECT",
                "DoNotRelist": "true"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MaxCost": "5000000",
                "DoNotRelist": "true",
                "amethyst0Gem": "PERFECT"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MaxCost": "5000000",
                "DoNotRelist": "true",
                "amethyst1Gem": "PERFECT"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MaxCost": "5000000",
                "DoNotRelist": "true",
                "sapphire0Gem": "PERFECT"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MaxCost": "5000000",
                "DoNotRelist": "true",
                "sapphire1Gem": "PERFECT"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MaxCost": "3000000",
                "DoNotRelist": "true",
                "ruby1Gem": "PERFECT"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MaxCost": "3000000",
                "DoNotRelist": "true",
                "ruby0Gem": "PERFECT"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "DRILL",
                "DrillPartEngine": "amber_polished_drill_engine",
                "StartingBid": "<140m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "DRILL",
                "DrillPartEngine": "sapphire_polished_drill_engine",
                "StartingBid": "<60m"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "DRILL",
                "StartingBid": "<90m",
                "DrillPartUpgradeModule": "goblin_omelette_blue_cheese"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemCategory": "DRILL",
                "StartingBid": "<60m",
                "DrillPartFuelTank": "perfectly_cut_fuel_tank"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "DrillPartEngine": "Any",
                "DrillPartFuelTank": "Any",
                "DrillPartUpgradeModule": "Any",
                "DoNotRelist": "true",
                "AfterMainFilter": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_DRILL",
            "displayName": "Divan's Drill",
            "filter": {
                "MaxCost": "1500000000",
                "DrillPartEngine": "amber_polished_drill_engine",
                "DrillPartFuelTank": "perfectly_cut_fuel_tank",
                "DrillPartUpgradeModule": "goblin_omelette_blue_cheese"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "ARTIFACT_OF_CONTROL",
            "displayName": "Artifact of Control",
            "filter": {
                "MinProfit": "75000000"
            },
            "tags": [
                "user"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "CraftCostWeight": "default:0.70,rarity_upgrades:0.05,hotpc:0.2,unlocked_slots:0.35,upgrade_level:0.4,skin:0.3,dye_item:0.2,gilded:0.2,festive:0.2,fabled:0.2,rooted:0.1,giant:0.2,jaded:0.3,auspicious:0.2,submerged:0.2,mossy:0.2,renowned:0.3,champion:0.2,prosecute:0.5,cleave:0.1,ender_slayer:0.2,strong_mana:0.3,green_thumb:0.2,pristine:0.2,compact:0.2,growth:0.1,protection:0.1,giant_killer:0.2,overload:0.2,hardened_mana:0.2,cultivating:0.15,smarty_pants:0.2,smite:0.2,hecatomb:0.1,dragon_hunter:0.2,efficiency:0.5,ultimate_soul_eater:0.2,ultimate_combo:0.2,ultimate_wise:0.6,ultimate_last_stand:0.3,ultimate_legion:0.5,ultimate_one_for_all:0,ultimate_wisdom:0.5,rune_grand_searing:0.15,winning_bid:0.1,full_bid:0.1,art_of_war_count:0.2,artofpeaceapplied:0.1,farming_for_dummies_count:0.3,divan_powder_coating:0.4,drill_part_fuel_tank:0.85,drill_part_upgrade_module:0.85,drill_part_engine:0.85,pgems:0.85",
                "MinProfit": "20000000",
                "MinProfitPercentage": "15"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfitPercentage": "16",
                "MinProfit": "20000000",
                "CraftCostWeight": "default:0.75,rarity_upgrades:0.05,hotpc:0.2,unlocked_slots:0.35,upgrade_level:0.4,skin:0.35,dye_item:0.05,gilded:0.2,festive:0.2,fabled:0.2,rooted:0.1,giant:0.2,jaded:0.3,auspicious:0.2,submerged:0.2,mossy:0.25,renowned:0.3,champion:0.2,prosecute:0.5,cleave:0.1,ender_slayer:0.2,strong_mana:0.2,green_thumb:0.2,pristine:0.2,compact:0.2,growth:0.1,protection:0.1,giant_killer:0.2,overload:0.2,hardened_mana:0.2,cultivating:0.2,smarty_pants:0.2,smite:0.2,hecatomb:0.1,dragon_hunter:0.2,efficiency:0.4,ultimate_soul_eater:0.2,ultimate_combo:0.2,ultimate_wise:0.6,ultimate_last_stand:0.3,ultimate_legion:0.5,ultimate_one_for_all:0,ultimate_wisdom:0.5,rune_grand_searing:0.3,winning_bid:0.1,full_bid:0.1,art_of_war_count:0.2,artofpeaceapplied:0.05,farming_for_dummies_count:0.15,divan_powder_coating:0.35,drill_part_fuel_tank:0.85,drill_part_upgrade_module:0.85,drill_part_engine:0.85,pgems:0.85"
            },
            "tags": [
                "test"
            ],
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "MinProfit": "3500000",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDER_DRAGON",
            "displayName": "Ender Dragon",
            "filter": {
                "StartingBid": "<255m",
                "PetLevel": "1-100",
                "DoNotRelist": "true"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "PET_ENDER_DRAGON",
            "displayName": "Ender Dragon",
            "filter": {
                "StartingBid": "<575m",
                "PetLevel": "1-100",
                "DoNotRelist": "true",
                "Rarity": "LEGENDARY",
                "PetItem": "NOT_TIER_BOOST"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": "DIVAN_DRILL",
            "displayName": "Divan's Drill",
            "filter": {
                "MaxCost": "870000000",
                "DrillPartEngine": "None",
                "DrillPartFuelTank": "None",
                "DrillPartUpgradeModule": "None"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        },
        {
            "tag": null,
            "displayName": null,
            "filter": {
                "ItemNameContains": "Bingo",
                "MinProfitPercentage": "15"
            },
            "tags": null,
            "order": 0,
            "group": null,
            "disabled": false
        }
    ],
    "lbin": false,
    "visibility": {
        "cost": true,
        "estProfit": true,
        "lbin": false,
        "slbin": false,
        "medPrice": true,
        "seller": false,
        "volume": true,
        "extraFields": 0,
        "avgSellTime": false,
        "profitPercent": true,
        "profit": false,
        "sellerOpenBtn": false,
        "lore": true,
        "links": true,
        "copySuccessMessage": true,
        "hideSold": false,
        "hideManipulated": false
    },
    "mod": {
        "justProfit": true,
        "soundOnFlip": true,
        "shortNumbers": true,
        "shortNames": false,
        "blockTenSecMsg": true,
        "format": "§d§kc§a§kc§b§kc§r§dSpirit§b§kc§a§kc§d§kc§r:§x  [menu]{1}{2} §c{4} {3}⇨ §a{5} §5§l(§0+{6} §5{7}%§0§l) §l§6 §3LBin: {9} §4☠ §bMed: {8}",
        "blockedFormat": null,
        "chat": false,
        "countdown": false,
        "hideNoBestFlip": false,
        "timerX": 0,
        "timerY": 0,
        "timerSeconds": 0,
        "timerScale": 0.0,
        "timerPrefix": null,
        "timerPrecision": 0,
        "blockedMsg": 0,
        "maxPercentOfPurse": 100,
        "noBedDelay": false,
        "streamerMode": false,
        "autoStartFlipper": true,
        "normalSoldFlips": false,
        "tempBlacklistSpam": false,
        "dataOnlyMode": false,
        "ahListHours": 0,
        "quickSell": false,
        "maxItemsInInventory": 0,
        "disableSpamProtection": false,
        "tempBlacklistThreshold": 20
    },
    "finders": 1143,
    "fastMode": false,
    "publishedAs": "SpiritFig",
    "loadedVersion": 103,
    "changer": "0794fc82-48f9-4ab4-8580-a71f5caf2479",
    "onlyBin": true,
    "whitelistAftermain": false,
    "basedConfig": null,
    "blockExport": false,
    "blockHighCompetition": false,
    "minProfit": 3000000,
    "minProfitPercent": 0,
    "minVolume": 0.0,
    "maxCost": 10000000000,
    "lastChange": "minprofit"
}
""";
    }
}