using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Opt-in end to end test against a real MariaDB read replica.
/// Set SKY_ACTIVE_E2E_DB to the connection string to run it.
/// </summary>
[Explicit("Needs SKY_ACTIVE_E2E_DB")]
public class ActiveAuctionEndToEndTests
{
    private const string EnvName = "SKY_ACTIVE_E2E_DB";

    private static void Log(string message) => NUnit.Framework.TestContext.Progress.WriteLine(message);

    [Test]
    public async Task LoadAndQueryRealActiveAuctions()
    {
        var connection = Environment.GetEnvironmentVariable(EnvName);
        if (string.IsNullOrEmpty(connection))
            Assert.Ignore($"{EnvName} not set");

        HypixelContext.SetConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DBConnection"] = connection,
            ["DBVersion"] = "10.11"
        }).Build());
        var ct = CancellationToken.None;

        var loader = new ActiveAuctionDbLoader();
        var sw = Stopwatch.StartNew();
        var bound = await loader.FindLowerBoundIdAsync(DateTime.UtcNow - TimeSpan.FromDays(15), ct);
        sw.Stop();
        Log($"lower bound id={bound} probe took {sw.ElapsedMilliseconds} ms");

        var set = new ActiveAuctionSet();
        var before = GC.GetTotalMemory(true);
        sw.Restart();
        await foreach (var a in loader.LoadActiveAsync(bound, DateTime.UtcNow, ct))
            set.Upsert(a, "db");
        sw.Stop();
        var loadSeconds = sw.Elapsed.TotalSeconds;
        set.MarkReady();
        var snap = set.GetFreshSnapshot();
        GC.Collect();
        var after = GC.GetTotalMemory(true);
        var delta = after - before;
        Log($"loaded {snap.Count} auctions in {loadSeconds:F1} s, memory delta {delta / 1024.0 / 1024.0:F1} MB, {(snap.Count == 0 ? 0 : delta / snap.Count)} bytes/auction");
        snap.Count.Should().BeGreaterThan(0);

        var filterEngine = new FilterEngine(new Mock<INBT>().Object);
        ActiveAuctionQueryResult Run(ActiveAuctionQuery q, string[] tags, Dictionary<string, string> filters)
        {
            var matcher = filters.Count == 0 ? null : filterEngine.GetMatcher(filters);
            return ActiveAuctionQueryEngine.Execute(snap, q, tags, filters, matcher);
        }

        // (a) armor set, cheapest 3 per piece
        var tagsA = new[] { "FINAL_DESTINATION_HELMET", "FINAL_DESTINATION_CHESTPLATE", "FINAL_DESTINATION_LEGGINGS", "FINAL_DESTINATION_BOOTS" };
        var resultA = Run(new ActiveAuctionQuery(new(), tagsA, null, null, null, ActiveSortOrder.LOWEST_PRICE, 0, 50, 3), tagsA, new());
        Log($"(a) totalCount={resultA.TotalCount} tagCounts={string.Join(", ", resultA.TagCounts.Select(t => $"{t.Key}={t.Value}"))}");
        foreach (var item in resultA.Items.Take(3))
            Log($"(a) {item.Tag} '{item.ItemName}' price={(item.HighestBidAmount == 0 ? item.StartingBid : item.HighestBidAmount)} end={item.End:u}");
        resultA.Items.Count.Should().BeLessThanOrEqualTo(12);

        // (b) enchant filter
        var filtersB = new Dictionary<string, string> { ["Enchantment"] = "sharpness", ["EnchantLvl"] = "7" };
        var swQuery = Stopwatch.StartNew();
        var resultB = Run(new ActiveAuctionQuery(filtersB, Array.Empty<string>(), null, null, null, ActiveSortOrder.LOWEST_PRICE, 0, 20, null), Array.Empty<string>(), filtersB);
        swQuery.Stop();
        Log($"(b) totalCount={resultB.TotalCount} items={resultB.Items.Count} took {swQuery.ElapsedMilliseconds} ms");
        swQuery.ElapsedMilliseconds.Should().BeLessThan(500);

        // (c) color filter
        var filtersC = new Dictionary<string, string> { ["Color"] = "FFFFFF" };
        var resultC = Run(new ActiveAuctionQuery(filtersC, Array.Empty<string>(), null, null, null, ActiveSortOrder.LOWEST_PRICE, 0, 20, null), Array.Empty<string>(), filtersC);
        Log($"(c) Color=FFFFFF totalCount={resultC.TotalCount}");

        // cross check (a) against the database
        using var context = new HypixelContext();
        var now = DateTime.UtcNow;
        var dbCount = await context.Auctions
            .Where(a => a.Id > bound && a.End > now && a.Tag.StartsWith("FINAL_DESTINATION_"))
            .CountAsync(ct);
        Log($"cross check: engine={resultA.TotalCount} db={dbCount}");
        var tolerance = (int)Math.Ceiling(Math.Max(2, dbCount * 0.05));
        Math.Abs(resultA.TotalCount - dbCount).Should().BeLessThanOrEqualTo(tolerance);
    }
}
