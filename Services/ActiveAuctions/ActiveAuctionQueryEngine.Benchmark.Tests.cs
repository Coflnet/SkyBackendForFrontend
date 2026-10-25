using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Moq;
using NUnit.Framework;
using TestContext = NUnit.Framework.TestContext;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>Deterministic synthetic auctions for benchmarks and property tests</summary>
internal static class SyntheticAuctions
{
    public static readonly string[] Colors = { "FFFFFF", "000000", "FF0000", "00FF00" };

    public static List<SaveAuction> Make(int count, int tagCount, int seed)
    {
        var rnd = new Random(seed);
        var tags = Enumerable.Range(0, tagCount).Select(i => "TAG_" + i).ToArray();
        var enchants = Enum.GetValues<Enchantment.EnchantmentType>()
            .Where(e => e != Enchantment.EnchantmentType.unknown && e != Enchantment.EnchantmentType.sharpness)
            .Take(39).Append(Enchantment.EnchantmentType.sharpness).ToArray(); // pool of 40
        var tiers = new[] { Tier.COMMON, Tier.UNCOMMON, Tier.RARE, Tier.EPIC, Tier.LEGENDARY, Tier.MYTHIC };
        var reforges = Enum.GetValues<ItemReferences.Reforge>().Where(r => r != ItemReferences.Reforge.Any).Take(30).ToArray();
        var end = DateTime.UtcNow.AddHours(2);
        var result = new List<SaveAuction>(count);
        for (var i = 1; i <= count; i++)
        {
            var a = new SaveAuction
            {
                UId = i,
                Uuid = i.ToString("x32"),
                Tag = tags[rnd.Next(tags.Length)],
                ItemName = "Item " + i,
                StartingBid = 1000 + rnd.Next(1_000_000),
                End = end.AddMinutes(rnd.Next(600)),
                Start = DateTime.UtcNow.AddMinutes(-rnd.Next(60)),
                AuctioneerId = "seller" + rnd.Next(2000),
                Bin = rnd.NextDouble() < 0.6,
                Tier = tiers[rnd.Next(tiers.Length)],
                Reforge = reforges[rnd.Next(reforges.Length)],
                Enchantments = new(),
                Bids = new(),
                FlatenedNBT = new Dictionary<string, string>
                {
                    ["uuid"] = Guid.NewGuid().ToString(),
                    ["hot_potato_count"] = rnd.Next(11).ToString(),
                    ["rarity_upgrades"] = "1"
                }
            };
            var n = rnd.Next(1, 4);
            for (var e = 0; e < n; e++)
            {
                var type = enchants[rnd.Next(enchants.Length)];
                if (a.Enchantments.Any(x => x.Type == type))
                    continue;
                a.Enchantments.Add(new Enchantment(type, (byte)rnd.Next(1, 8)));
            }
            if (rnd.NextDouble() < 0.2)
            {
                a.FlatenedNBT.Remove("rarity_upgrades");
                a.FlatenedNBT["color"] = Colors[rnd.Next(Colors.Length)] is var c && rnd.Next(2) == 0
                    ? $"{Convert.ToInt32(c.Substring(0, 2), 16)}:{Convert.ToInt32(c.Substring(2, 2), 16)}:{Convert.ToInt32(c.Substring(4, 2), 16)}"
                    : "255:255:255";
            }
            result.Add(a);
        }
        return result;
    }

    public static ActiveAuctionSet ToSet(IEnumerable<SaveAuction> auctions)
    {
        var set = new ActiveAuctionSet();
        foreach (var a in auctions)
            set.Upsert(a, "bench");
        return set;
    }
}

public class ActiveAuctionQueryEngineBenchmark
{
    [Test, Explicit("benchmark, run on demand")]
    public void MeasureCommonFilterShapes()
    {
        var auctions = SyntheticAuctions.Make(70_000, 400, 42);
        var set = SyntheticAuctions.ToSet(auctions);
        var sw = Stopwatch.StartNew();
        var snapshot = set.GetFreshSnapshot();
        sw.Stop();
        TestContext.Progress.WriteLine($"snapshot first build (70k): {sw.Elapsed.TotalMilliseconds:F1} ms");
        var rebuilds = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var extra = SyntheticAuctions.Make(1, 1, 100 + i)[0];
            extra.UId = 1_000_000 + i;
            set.Upsert(extra, "bench");
            sw.Restart();
            snapshot = set.GetFreshSnapshot();
            rebuilds.Add(sw.Elapsed.TotalMilliseconds);
        }
        TestContext.Progress.WriteLine($"snapshot rebuild after 1 change: median {Median(rebuilds):F1} ms max {rebuilds.Max():F1} ms");

        var engine = new FilterEngine(new Mock<INBT>().Object);
        var usedReforge = snapshot.ByReforge.OrderByDescending(p => p.Value.Length).First().Key.ToString();
        var tagSet = snapshot.ByTag.Keys.Take(4).ToArray();
        var cases = new List<(string Name, string[] Tags, Dictionary<string, string> Filters)>
        {
            ("A Enchantment=sharpness EnchantLvl=7", Array.Empty<string>(), new() { ["Enchantment"] = "sharpness", ["EnchantLvl"] = "7" }),
            ("B sharpness=7", Array.Empty<string>(), new() { ["sharpness"] = "7" }),
            ("C Rarity=LEGENDARY Bin=true", Array.Empty<string>(), new() { ["Rarity"] = "LEGENDARY", ["Bin"] = "true" }),
            ("D Color=FFFFFF", Array.Empty<string>(), new() { ["Color"] = "FFFFFF" }),
            ("E 4 tags + Reforge", tagSet, new() { ["Reforge"] = usedReforge }),
        };
        var query = new ActiveAuctionQuery(new(), null, null, null, null, ActiveSortOrder.LOWEST_PRICE, 0, 20, null, null);
        foreach (var (name, tags, filters) in cases)
        {
            sw.Restart();
            var matcher = engine.GetMatcher(filters);
            var compile = sw.Elapsed.TotalMilliseconds;
            foreach (var narrow in new[] { false, true })
            {
                sw.Restart();
                var result = ActiveAuctionQueryEngine.Execute(snapshot, query, tags, filters, matcher, narrow);
                var cold = sw.Elapsed.TotalMilliseconds;
                var warm = new List<double>();
                for (var i = 0; i < 20; i++)
                {
                    sw.Restart();
                    ActiveAuctionQueryEngine.Execute(snapshot, query, tags, filters, matcher, narrow);
                    warm.Add(sw.Elapsed.TotalMilliseconds);
                }
                TestContext.Progress.WriteLine($"{name} [{(narrow ? "narrowed" : "full scan")}] matches={result.TotalCount} compile={compile:F2} ms first={cold:F2} ms warm median={Median(warm):F2} ms max={warm.Max():F2} ms");
            }
        }
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
