using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class ActiveAuctionQueryEngineTests
{
    private static readonly string[] Tags = { "A_ONE", "B_TWO", "C_THREE", "D_FOUR", "E_FIVE", "F_SIX" };
    private static readonly DateTime Now = DateTime.UtcNow;
    private ActiveAuctionSnapshot snapshot;
    private FilterEngine filterEngine;

    [SetUp]
    public void Setup()
    {
        filterEngine = new FilterEngine(new Mock<INBT>().Object);
        var set = new ActiveAuctionSet();
        for (var i = 1; i <= 500; i++)
        {
            var tag = Tags[i % Tags.Length];
            var a = new SaveAuction
            {
                UId = i,
                Uuid = i.ToString("x32"),
                Tag = tag,
                ItemName = (i % 2 == 0 ? "Fancy " : "plain ") + tag,
                StartingBid = 100 + (i % 10) * 10, // many price ties
                End = Now.AddMinutes(10 + (i * 7) % 300),
                Start = Now.AddMinutes(-(i % 50)),
                AuctioneerId = "seller" + (i % 5),
                Bin = true,
                Tier = Tier.EPIC,
                Enchantments = new(),
                Bids = new(),
                FlatenedNBT = new Dictionary<string, string>()
            };
            if (i % 25 == 0)
                a.Enchantments.Add(new Enchantment(Enchantment.EnchantmentType.sharpness, 7));
            else if (i % 5 == 0)
                a.Enchantments.Add(new Enchantment(Enchantment.EnchantmentType.sharpness, 5));
            if (i % 40 == 0)
                a.FlatenedNBT["color"] = "255:0:0";
            set.Upsert(a, "test").Should().BeTrue();
        }
        snapshot = set.GetFreshSnapshot();
    }

    private static ActiveAuctionQuery Q(ActiveSortOrder order = ActiveSortOrder.LOWEST_PRICE, int page = 0, int size = 20, int? perTag = null,
        string contains = null, string seller = null)
        => new(new(), null, null, null, contains, order, page, size, perTag, seller);

    private ActiveAuctionQueryResult Run(ActiveAuctionQuery q, string[] tags = null, Dictionary<string, string> filters = null)
    {
        filters ??= new();
        var matcher = filters.Count == 0 ? null : filterEngine.GetMatcher(filters);
        return ActiveAuctionQueryEngine.Execute(snapshot, q, tags ?? Array.Empty<string>(), filters, matcher);
    }

    [Test]
    public void LowestPriceIsDeterministicWithUidTieBreak()
    {
        var first = Run(Q());
        var again = Run(Q());
        first.Items.Select(i => i.Uuid).Should().Equal(again.Items.Select(i => i.Uuid));
        first.TotalCount.Should().Be(500);
        first.Items.Select(i => i.StartingBid).Should().BeInAscendingOrder();
        // all with the cheapest price, ordered by uid
        var uids = first.Items.Select(i => Convert.ToInt64(i.Uuid, 16)).ToList();
        uids.Should().BeInAscendingOrder();
        var page1 = Run(Q(page: 1));
        page1.Items.Select(i => i.Uuid).Should().NotIntersectWith(first.Items.Select(i => i.Uuid));
        page1.Items.First().StartingBid.Should().BeGreaterThanOrEqualTo(first.Items.Last().StartingBid);
    }

    [Test]
    public void HighestPriceSortsDescending()
    {
        Run(Q(ActiveSortOrder.HIGHEST_PRICE)).Items.Select(i => i.StartingBid).Should().BeInDescendingOrder();
    }

    [Test]
    public void BidPriceIsUsedWhenPresent()
    {
        var a = snapshot.All[0];
        ActiveAuctionQueryEngine.Price(a).Should().Be(a.StartingBid);
        a.HighestBidAmount = 99999;
        ActiveAuctionQueryEngine.Price(a).Should().Be(99999);
        a.HighestBidAmount = 0;
    }

    [Test]
    public void EndingSoonAndNewest()
    {
        Run(Q(ActiveSortOrder.ENDING_SOON)).Items.Select(i => i.End).Should().BeInAscendingOrder();
        var newest = Run(Q(ActiveSortOrder.NEWEST)).Items;
        newest.Select(i => i.Start).Should().BeInDescendingOrder();
    }

    [Test]
    public void PerTagLimitKeepsFirstKPerTagButCountsAll()
    {
        var tags = Tags.Take(4).ToArray();
        var result = Run(Q(perTag: 3, size: 100), tags);
        result.Items.Should().HaveCount(12);
        result.Items.GroupBy(i => i.Tag).Should().OnlyContain(g => g.Count() == 3);
        result.TagCounts.Keys.Should().BeEquivalentTo(tags);
        result.TagCounts.Values.Sum().Should().Be(result.TotalCount);
        result.TagCounts.Values.Should().OnlyContain(c => c > 3);
        result.ResolvedTags.Should().BeEquivalentTo(tags);
    }

    [Test]
    public void ItemNameContainsIsCaseInsensitive()
    {
        var result = Run(Q(contains: "FANCY", size: 100));
        result.TotalCount.Should().Be(250);
        result.Items.Should().OnlyContain(i => i.ItemName.StartsWith("Fancy"));
    }

    [Test]
    public void TotalCountDoesNotDependOnPaging()
    {
        Run(Q(size: 5)).TotalCount.Should().Be(500);
        Run(Q(page: 3, size: 7)).TotalCount.Should().Be(500);
        Run(Q(page: 1000)).Items.Should().BeEmpty();
    }

    [Test]
    public void SellerQueryUsesSellerIndex()
    {
        var result = Run(Q(seller: "seller2", size: 100));
        result.TotalCount.Should().Be(100);
        result.Items.Should().OnlyContain(i => i.AuctioneerId == "seller2");
    }

    [Test]
    public void EnchantFilterReturnsOnlyMatchesWithHighlight()
    {
        var filters = new Dictionary<string, string> { ["Enchantment"] = "sharpness", ["EnchantLvl"] = "7" };
        var result = Run(Q(size: 100), null, filters);
        result.TotalCount.Should().Be(20);
        result.Items.Should().OnlyContain(i => i.Highlights["sharpness"] == "7");
    }

    [Test]
    public void PerEnchantKeyFilterWorksAndHighlights()
    {
        var filters = new Dictionary<string, string> { ["sharpness"] = "7" };
        var result = Run(Q(size: 100), null, filters);
        result.TotalCount.Should().Be(20);
        result.Items.Should().OnlyContain(i => i.Highlights["sharpness"] == "7");
    }

    [Test]
    public void ColorFilterReturnsOnlyMatchesWithHighlight()
    {
        var filters = new Dictionary<string, string> { ["Color"] = "255:0:0" };
        var result = Run(Q(size: 100), null, filters);
        result.TotalCount.Should().Be(12);
        result.Items.Should().OnlyContain(i => i.Highlights["Color"] == "255:0:0");
    }

    [Test]
    public void HighlightsAreCapped()
    {
        var a = snapshot.All[0];
        var filters = Enumerable.Range(0, 10).ToDictionary(i => "k" + i, i => "v");
        foreach (var k in filters.Keys)
            a.FlatenedNBT[k] = "x";
        HighlightExtractor.Extract(a, filters).Should().HaveCount(HighlightExtractor.MaxHighlights);
        foreach (var k in filters.Keys)
            a.FlatenedNBT.Remove(k);
    }
}
