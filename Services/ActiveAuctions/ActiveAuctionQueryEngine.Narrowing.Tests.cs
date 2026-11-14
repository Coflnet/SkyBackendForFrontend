using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class ActiveAuctionQueryEngineNarrowingTests
{
    private ActiveAuctionSnapshot snapshot;
    private FilterEngine filterEngine;
    private static readonly ActiveAuctionQuery AllQuery = new(new(), null, null, null, null, ActiveSortOrder.LOWEST_PRICE, 0, 100, null, null);

    [OneTimeSetUp]
    public void Setup()
    {
        filterEngine = new FilterEngine(new Mock<INBT>().Object);
        snapshot = SyntheticAuctions.ToSet(SyntheticAuctions.Make(2000, 15, 7)).GetFreshSnapshot();
    }

    private static Dictionary<string, string> F(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    private void AssertSame(Dictionary<string, string> filters, params string[] tags)
    {
        var matcher = filterEngine.GetMatcher(filters);
        var narrowed = ActiveAuctionQueryEngine.Execute(snapshot, AllQuery, tags, filters, matcher, true);
        var full = ActiveAuctionQueryEngine.Execute(snapshot, AllQuery, tags, filters, matcher, false);
        narrowed.TotalCount.Should().Be(full.TotalCount);
        narrowed.Items.Select(i => i.Uuid).Should().Equal(full.Items.Select(i => i.Uuid));
        narrowed.TagCounts.Should().BeEquivalentTo(full.TagCounts);
    }

    [TestCase("Color", "FFFFFF")]
    [TestCase("Color", "FF0000,00FF00")]
    [TestCase("Color", "FF0000, 00FF00")]
    [TestCase("Color", "255:0:0")]
    [TestCase("Color", "255:255:255")]
    [TestCase("Color", "FFFFFF-10")]
    [TestCase("Color", "pattern:AABBCC")]
    [TestCase("Color", "Any")]
    [TestCase("Color", "None")]
    [TestCase("HexColorList", "FFFFFF,000000")]
    public void ColorFormsGiveIdenticalResults(string key, string value)
    {
        AssertSame(F((key, value)));
    }

    [Test]
    public void ExactColorNarrowsToValueIndex()
    {
        var exact = ActiveAuctionQueryEngine.NarrowingLists(snapshot, F(("Color", "FFFFFF"))).Single();
        exact.Length.Should().BeLessThan(snapshot.WithColor.Length);
        exact.Should().Equal(snapshot.ByColorValue[Convert.ToInt32("FFFFFF", 16) << 8]);
        ActiveAuctionQueryEngine.NarrowingLists(snapshot, F(("Color", "FFFFFF-10"))).Single().Should().BeSameAs(snapshot.WithColor);
        ActiveAuctionQueryEngine.NarrowingLists(snapshot, F(("Color", "pattern:AABBCC"))).Single().Should().BeSameAs(snapshot.WithColor);
    }

    [Test]
    public void CommonShapesGiveIdenticalResults()
    {
        var reforge = snapshot.ByReforge.OrderByDescending(p => p.Value.Length).First().Key.ToString();
        AssertSame(F(("Enchantment", "sharpness"), ("EnchantLvl", "7")));
        AssertSame(F(("Enchantment", "SHARPNESS")));
        AssertSame(F(("sharpness", "7")));
        AssertSame(F(("Rarity", "LEGENDARY"), ("Bin", "true")));
        AssertSame(F(("Bin", "false")));
        AssertSame(F(("Color", "FFFFFF")));
        AssertSame(F(("Reforge", reforge)), snapshot.ByTag.Keys.Take(4).ToArray());
        AssertSame(F(("Enchantment", "sharpness"), ("Rarity", "LEGENDARY"), ("Bin", "true")), snapshot.ByTag.Keys.Take(5).ToArray());
    }

    [Test]
    public void NarrowingActuallyPrunes()
    {
        var lists = ActiveAuctionQueryEngine.NarrowingLists(snapshot, F(("Enchantment", "sharpness"), ("EnchantLvl", "7")));
        lists.Should().HaveCount(1);
        lists[0].Length.Should().BeLessThan(snapshot.Count / 2);
        ActiveAuctionQueryEngine.NarrowingLists(snapshot, F(("Rarity", "LEGENDARY"), ("Bin", "true"), ("Color", "FFFFFF"))).Should().HaveCount(3);
        ActiveAuctionQueryEngine.NarrowingLists(snapshot, F(("sharpness", "7"))).Should().HaveCount(1);
    }

    [TestCase("Enchantment", "None")]
    [TestCase("Enchantment", "none")]
    [TestCase("Enchantment", "sharpness,smite")]
    [TestCase("Color", "Any")]
    [TestCase("Color", "None")]
    [TestCase("Rarity", "Any")]
    [TestCase("Rarity", "LEGENDARY,EPIC")]
    [TestCase("Reforge", "Any")]
    [TestCase("Reforge", "Sharp,Wise")]
    [TestCase("Bin", "false")]
    [TestCase("sharpness", "0")]
    [TestCase("sharpness", "5-7")]
    [TestCase("sharpness", "<3")]
    public void UnparsableOrBroadValuesDoNotNarrow(string key, string value)
    {
        ActiveAuctionQueryEngine.NarrowingLists(snapshot, F((key, value))).Should().BeEmpty();
    }

    [Test]
    public void NonNumericEnchantLevelDoesNotNarrowEnchantment()
    {
        ActiveAuctionQueryEngine.NarrowingLists(snapshot, F(("Enchantment", "sharpness"), ("EnchantLvl", ">3"))).Should().BeEmpty();
    }

    [Test]
    public void Intersect()
    {
        var result = ActiveAuctionQueryEngine.Intersect(new() { new[] { 1, 3, 5, 7, 9 }, new[] { 3, 4, 5, 9, 11 }, new[] { 0, 3, 9 } });
        result.Should().Equal(3, 9);
        ActiveAuctionQueryEngine.Intersect(new() { new[] { 1, 2 }, Array.Empty<int>() }).Should().BeEmpty();
    }

    /// <summary>Compares both code paths for random filter combinations, narrowing must only prune</summary>
    [Test]
    public void RandomFilterCombinationsMatchUnnarrowedEngine()
    {
        var rnd = new Random(123);
        var enchants = new[] { "sharpness", "looting", "smite", "None", "Any", "sharpness,smite" };
        var reforges = snapshot.ByReforge.Keys.Select(r => r.ToString()).Concat(new[] { "Any", "None" }).ToArray();
        var tiers = new[] { "LEGENDARY", "EPIC", "COMMON", "Any" };
        var colors = new[] { "FFFFFF", "Any", "FFFFFF,000000", "255:255:255" };
        var tags = snapshot.ByTag.Keys.ToArray();
        for (var n = 0; n < 150; n++)
        {
            var filters = new Dictionary<string, string>();
            if (rnd.Next(2) == 0)
            {
                filters["Enchantment"] = enchants[rnd.Next(enchants.Length)];
                if (rnd.Next(2) == 0)
                    filters["EnchantLvl"] = rnd.Next(1, 8).ToString();
            }
            if (rnd.Next(4) == 0) filters["sharpness"] = rnd.Next(1, 8).ToString();
            if (rnd.Next(3) == 0) filters["Rarity"] = tiers[rnd.Next(tiers.Length)];
            if (rnd.Next(3) == 0) filters["Reforge"] = reforges[rnd.Next(reforges.Length)];
            if (rnd.Next(3) == 0) filters["Bin"] = rnd.Next(2) == 0 ? "true" : "false";
            if (rnd.Next(4) == 0) filters["Color"] = colors[rnd.Next(colors.Length)];
            if (filters.Count == 0)
                continue;
            var pick = rnd.Next(3) == 0 ? tags.OrderBy(_ => rnd.Next()).Take(rnd.Next(1, 5)).ToArray() : Array.Empty<string>();
            Func<SaveAuction, bool> matcher;
            try { matcher = filterEngine.GetMatcher(filters); }
            catch (Exception) { continue; } // filter combination the engine rejects, nothing to compare
            var narrowed = ActiveAuctionQueryEngine.Execute(snapshot, AllQuery, pick, filters, matcher, true);
            var full = ActiveAuctionQueryEngine.Execute(snapshot, AllQuery, pick, filters, matcher, false);
            narrowed.TotalCount.Should().Be(full.TotalCount, string.Join(",", filters.Select(f => f.Key + "=" + f.Value)));
            narrowed.Items.Select(i => i.Uuid).Should().Equal(full.Items.Select(i => i.Uuid));
        }
    }
}
