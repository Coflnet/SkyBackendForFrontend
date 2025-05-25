using System.Collections.Generic;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using FluentAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class CraftCostWeightTests
{
    [TestCase("2", "ethermerge:1,default:0.8", true, 50908000)]
    [TestCase("50000000", "ethermerge:1,default:0.8", false, 0)]
    [TestCase("2", "ethermerge:1,aoteStone:0.1,default:0.8", true, 43908000)]
    [TestCase("2", "art_of_war_count:0.5,default:0.8", true, 45228000)]
    [TestCase("2", "art_of_war_count:0.5,art_of_war_count.1:1.5,default:0.8", true, 53428000)]
    [TestCase("2", "rarity_upgrades:0.1,default:0", true, 10820000)]
    [TestCase("2", "default:0.1", true, 15018500)]
    [TestCase("2", "default:0.1,cleanItem:0.9", true, 14018500)]
    [TestCase("4100000", "default:0", false, 0)]
    public void Test(string minProfit, string filterVal, bool expected, int target)
    {
        var filter = new CraftCostWeightDetailedFlipFilter();
        var expression = filter.GetExpression(new(new() { { "MinProfit", minProfit } }, null), filterVal);
        var compiled = expression.Compile();
        FlipInstance flip = GetSampleFlip();
        compiled.Invoke(flip).Should().Be(expected);
        if (target != 0)
            flip.Context["target"].Should().Be(target.ToString());
    }

    [TestCase("2000", "art_of_war_count:0.5,default:0.8", false, 0)]
    [TestCase("2", "art_of_war_count:0.5,default:0.8", true, 45228000)]
    public void TestProfitPerent(string minProfitPercent, string filterVal, bool expected, int target)
    {
        var filter = new CraftCostWeightDetailedFlipFilter();
        var expression = filter.GetExpression(new(new() { { "MinProfitPercentage", minProfitPercent } }, null), filterVal);
        var compiled = expression.Compile();
        FlipInstance flip = GetSampleFlip();
        compiled.Invoke(flip).Should().Be(expected);
        if (target != 0)
            flip.Context["target"].Should().Be(target.ToString());
    }

    private static FlipInstance GetSampleFlip()
    {
        return new FlipInstance()
        {
            Target = 50908000,
            Finder = LowPricedAuction.FinderType.CraftCost,
            Auction = new Core.SaveAuction() { StartingBid = 10000000, FlatenedNBT = new() { { "art_of_war_count", "1" } }, Enchantments = [] },
            Context = new() {
                {"cleanCost", "10000000"},
                { "breakdown", "{\"ethermerge\":16100000,\"aoteStone\":10000000,\"rarity_upgrades\":8200000,\"art_of_war_count\":8200000,\"hotpc\":7685000}" } }
        };
    }

    [TestCase("default:.6 & hpc:0.6 & ultimate_wisdom:.4 & ultimate_legion:.4 & renowned:.5 & pesterminator:.3 & hecatomb:.3", "Use commas to separate multipliers, not `&`, also don't put unnecessary spaces anywhere")]
    [TestCase("hpc:0.6", "Invalid modifier `hpc` provided, did you mean `hotpc`?")]
    [TestCase("hotpc:0.6", "No default multiplier provided, use default:0.9 to disable")]
    [TestCase("default:0.6,pristine:0.6,pristine:0.2", "Dupplicate weight in 'default:0.6,pristine...' for pristine")]
    [TestCase("default:0.45,bane_of_Arthropods0.2,delicate:0.2", "DoubleDot (:) missing in filter at 'bane_of_Arthropods0.2'")]
    public void ExpectedErrors(string filterVal, string expected)
    {
        var filter = new CraftCostWeightDetailedFlipFilter();
        filter.Invoking(f => f.GetExpression(new(new() { { "MinProfit", "2" } }, null), filterVal)).Should().Throw<CoflnetException>().WithMessage(expected);
    }

    [Test]
    public void OverrideDefaultPropertyWeightIfSpecifiedDefaultIsLower()
    {
        var filter = new CraftCostWeightDetailedFlipFilter();
        var expression = filter.GetExpression(new(new() { { "MinProfit", "2" } }, null), "ethermerge:1,default:0.5");
        var compiled = expression.Compile();
        FlipInstance flip = GetSampleFlip();
        flip.Context["cleanCost"] = "18000000";
        flip.Context["breakdown"] = """
        {"upgrade_level":172684180,"unlocked_slots":25344299,"ultimate_legion":15000001,"rarity_upgrades":8781214,"hotpc":7101810}
        """;
        // default weight for upgrade_level is 0.8 and should be overriden to 0.5
        compiled.Invoke(flip).Should().BeTrue();
        flip.Context["target"].Should().Be("132455752");
    }


    [Test]
    public void End2End()
    {
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(null));
        var flipSettings = new FlipSettings()
        {
            WhiteList = new List<ListEntry>(){
                new ListEntry(){
                    filter = new (){
                        {"CraftCostWeight", "ethermerge:0.2,default:0.8"},
                        { "MinProfit", "2m" }
                    }
                }
            },
            AllowedFinders = LowPricedAuction.FinderType.CraftCost
        };
        var flip = GetSampleFlip();
        flipSettings.MatchesSettings(flip).Item1.Should().BeTrue();
        flip.Target.Should().Be(38028000L);
        flip.Auction.StartingBid = 37000000;
        flipSettings.MatchesSettings(flip).Item1.Should().BeFalse();
    }
}