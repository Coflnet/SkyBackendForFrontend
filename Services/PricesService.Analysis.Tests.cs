using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Regression tests for the pure, DB-free computation methods behind the sold-auction "advanced analysis"
/// and the live market analysis. Kept separate from PricesServiceTests (PricesService.Test.cs)
/// because that file's name does not match the csproj's `**\*.Tests.cs` Release exclusion glob.
/// </summary>
public class PricesServiceAnalysisTests
{
    private static PricesService.AnalysisSample Sold(long price, DateTime start, DateTime end, bool isBin = false, int sellerId = 0, int buyerId = 0)
        => new(price, start, end, isBin, sellerId, buyerId);

    private static PricesService.LiveListingSample Listing(long startingBid, bool bin, DateTime start, int sellerId = 0, int count = 1, long highestBidAmount = 0)
        => new(startingBid, highestBidAmount, count, start, bin, sellerId);

    // ---------------------------------------------------------------------
    // ComputeAdvancedAnalysis (sold auctions)
    // ---------------------------------------------------------------------

    [Test]
    public void SoldAnalysis_EmptyList_ReturnsEmptyResult()
    {
        var result = PricesService.ComputeAdvancedAnalysis(new List<PricesService.AnalysisSample>(), DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        result.TotalSales.Should().Be(0);
        result.VolumeBuckets.Should().BeEmpty();
        result.SellSpeedBuckets.Should().BeEmpty();
        result.TopSellers.Should().BeEmpty();
        result.MinPrice.Should().Be(0);
        result.MaxPrice.Should().Be(0);
        result.TopBuyers.Should().BeEmpty();
        result.UniqueBuyers.Should().Be(0);
        result.UniqueSellers.Should().Be(0);
    }

    [Test]
    public void SoldAnalysis_NullList_ReturnsEmptyResultWithoutThrowing()
    {
        var result = PricesService.ComputeAdvancedAnalysis(null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        result.TotalSales.Should().Be(0);
    }

    [Test]
    public void SoldAnalysis_BucketCounts_SumToSampleCount()
    {
        var end = new DateTime(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddSeconds(-1000), end),
            Sold(100, end.AddSeconds(-3000), end),
            Sold(110, end.AddSeconds(-500), end),
            Sold(250, end.AddDays(-10), end, isBin: true),
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        result.VolumeBuckets.Sum(b => b.Count).Should().Be(samples.Count);
        result.SellSpeedBuckets.Sum(b => b.SampleCount).Should().Be(samples.Count);
    }

    [Test]
    public void SoldAnalysis_MinAndMaxPrice_ComeFromSamples()
    {
        var end = DateTime.UtcNow;
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(37, end.AddSeconds(-10), end),
            Sold(9001, end.AddSeconds(-20), end),
            Sold(412, end.AddSeconds(-30), end),
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        result.MinPrice.Should().Be(37);
        result.MaxPrice.Should().Be(9001);
    }

    /// <summary>
    /// A small hand-computed dataset: prices 100 (x2), 110, 250 give a range of 150, so with 15 volume
    /// buckets (width 10) and 10 speed buckets (width 15) every index, boundary and average can be
    /// verified by hand instead of just asserting "it ran".
    /// </summary>
    [Test]
    public void SoldAnalysis_KnownDataset_ProducesExpectedBucketsAndSpeedCategories()
    {
        var end = new DateTime(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var start = end.AddDays(-1);
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddSeconds(-1000), end), // sellTime 1000
            Sold(100, end.AddSeconds(-3000), end), // sellTime 3000
            Sold(110, end.AddSeconds(-500), end),  // sellTime 500
            Sold(250, end.AddDays(-10), end),      // sellTime 10 days -> capped to 7 days
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, start, end);

        result.MinPrice.Should().Be(100);
        result.MaxPrice.Should().Be(250);
        result.TotalSales.Should().Be(4);

        // Volume buckets: width = (250-100)/15 = 10
        var bucket0 = result.VolumeBuckets.Single(b => b.MinPrice == 100);
        bucket0.MaxPrice.Should().Be(110);
        bucket0.Count.Should().Be(2);
        bucket0.AvgPrice.Should().Be(100);

        var bucket1 = result.VolumeBuckets.Single(b => b.MinPrice == 110);
        bucket1.MaxPrice.Should().Be(120);
        bucket1.Count.Should().Be(1);
        bucket1.AvgPrice.Should().Be(110);

        var lastVolumeBucket = result.VolumeBuckets.Single(b => b.MinPrice == 240);
        lastVolumeBucket.MaxPrice.Should().Be(250);
        lastVolumeBucket.Count.Should().Be(1);
        lastVolumeBucket.AvgPrice.Should().Be(250);

        // Speed buckets: width = (250-100)/10 = 15. Median sell time = 3000 (sorted: 500,1000,3000,604800).
        var speedBucket0 = result.SellSpeedBuckets.Single(b => b.MinPrice == 100);
        speedBucket0.SampleCount.Should().Be(3); // prices 100,100,110 all land in [100,115)
        speedBucket0.AvgSellTimeSeconds.Should().Be(1500); // (1000+3000+500)/3
        speedBucket0.SpeedCategory.Should().Be("MED"); // 1500 == median*0.5, not < it -> MED

        var lastSpeedBucket = result.SellSpeedBuckets.Single(b => b.MinPrice == 235);
        lastSpeedBucket.MaxPrice.Should().Be(250);
        lastSpeedBucket.SampleCount.Should().Be(1);
        lastSpeedBucket.AvgSellTimeSeconds.Should().Be(7 * 86400);
        lastSpeedBucket.SpeedCategory.Should().Be("VERY_SLOW");
    }

    [Test]
    public void SoldAnalysis_SellTime_IsCappedAtSevenDays()
    {
        var end = DateTime.UtcNow;
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddDays(-30), end), // way beyond 7 days
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        result.AvgSellTimeSeconds.Should().Be(7 * 86400);
        result.SellSpeedBuckets.Single().AvgSellTimeSeconds.Should().Be(7 * 86400);
    }

    [Test]
    public void SoldAnalysis_AllSamePrice_DoesNotThrowAndUsesSingleBucket()
    {
        var end = DateTime.UtcNow;
        var samples = Enumerable.Range(0, 5)
            .Select(i => Sold(100, end.AddSeconds(-i * 10), end))
            .ToList();

        Action act = () => PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        act.Should().NotThrow();
        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);
        result.VolumeBuckets.Should().HaveCount(1);
        result.VolumeBuckets.Single().Count.Should().Be(5);
        result.MinPrice.Should().Be(100);
        result.MaxPrice.Should().Be(100);
    }

    [Test]
    public void SoldAnalysis_DistinctBuyerCount_CountsUniqueBuyersNotSales()
    {
        var end = DateTime.UtcNow;
        // 5 sales by 3 different buyers (buyer 1 and 2 win twice each, buyer 3 once).
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddSeconds(-10), end, buyerId: 1),
            Sold(110, end.AddSeconds(-20), end, buyerId: 1),
            Sold(120, end.AddSeconds(-30), end, buyerId: 2),
            Sold(130, end.AddSeconds(-40), end, buyerId: 2),
            Sold(140, end.AddSeconds(-50), end, buyerId: 3),
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        result.UniqueBuyers.Should().Be(3);
        result.TotalSales.Should().Be(5);
    }

    [Test]
    public void SoldAnalysis_BuyerIdZero_IsIgnoredInCountAndTopBuyers()
    {
        var end = DateTime.UtcNow;
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddSeconds(-10), end, buyerId: 0), // missing/unknown buyer
            Sold(110, end.AddSeconds(-20), end, buyerId: 0),
            Sold(120, end.AddSeconds(-30), end, buyerId: 5),
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        result.UniqueBuyers.Should().Be(1);
        result.TopBuyers.Should().ContainSingle();
        result.TopBuyers.Single().Buyer.Should().Be("5");
        result.TopBuyers.Should().NotContain(b => b.Buyer == "0");
    }

    [Test]
    public void SoldAnalysis_UniqueSellers_CountsDistinctSellersAndIgnoresZero()
    {
        var end = DateTime.UtcNow;
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddSeconds(-10), end, sellerId: 1),
            Sold(110, end.AddSeconds(-20), end, sellerId: 1),
            Sold(120, end.AddSeconds(-30), end, sellerId: 2),
            Sold(130, end.AddSeconds(-40), end, sellerId: 0), // unknown seller, excluded
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        result.UniqueSellers.Should().Be(2);
    }

    [Test]
    public void SoldAnalysis_TopBuyers_OrderingCountsAndPercentages()
    {
        var end = DateTime.UtcNow;
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddSeconds(-10), end, buyerId: 1),
            Sold(110, end.AddSeconds(-20), end, buyerId: 1),
            Sold(120, end.AddSeconds(-30), end, buyerId: 1),
            Sold(130, end.AddSeconds(-40), end, buyerId: 2),
            Sold(140, end.AddSeconds(-50), end, buyerId: 3),
        };

        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        result.TopBuyers.Should().HaveCount(3);
        result.TopBuyers[0].Buyer.Should().Be("1");
        result.TopBuyers[0].Count.Should().Be(3);
        result.TopBuyers[0].Percentage.Should().BeApproximately(60, 0.01); // 3/5

        var others = result.TopBuyers.Skip(1).ToList();
        others.Should().Contain(b => b.Buyer == "2" && b.Count == 1);
        others.Should().Contain(b => b.Buyer == "3" && b.Count == 1);
        others.Should().OnlyContain(b => Math.Abs(b.Percentage - 20) < 0.01); // 1/5 each
    }

    [Test]
    public void SoldAnalysis_SamplesWithoutBuyerId_DefaultToZeroBuyersAndDoNotThrow()
    {
        var end = DateTime.UtcNow;
        // Built without specifying buyerId at all, relying on the AnalysisSample default value.
        var samples = new List<PricesService.AnalysisSample>
        {
            Sold(100, end.AddSeconds(-10), end),
            Sold(110, end.AddSeconds(-20), end),
        };

        Action act = () => PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);

        act.Should().NotThrow();
        var result = PricesService.ComputeAdvancedAnalysis(samples, end.AddDays(-1), end);
        result.UniqueBuyers.Should().Be(0);
        result.TopBuyers.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // ComputeLiveMarketAnalysis (active listings)
    // ---------------------------------------------------------------------

    [Test]
    public void LiveAnalysis_EmptyInput_ReturnsEmptyResult()
    {
        var result = PricesService.ComputeLiveMarketAnalysis(new List<PricesService.LiveListingSample>(), DateTime.UtcNow);

        result.BinCount.Should().Be(0);
        result.AuctionCount.Should().Be(0);
        result.SellerCount.Should().Be(0);
        result.LowestBin.Should().Be(0);
        result.MedianBin.Should().Be(0);
        result.HighestBin.Should().Be(0);
        result.PriceBuckets.Should().BeEmpty();
        result.TimeOnMarketBuckets.Should().BeEmpty();
    }

    [Test]
    public void LiveAnalysis_NullInput_ReturnsEmptyResultWithoutThrowing()
    {
        var result = PricesService.ComputeLiveMarketAnalysis(null, DateTime.UtcNow);

        result.BinCount.Should().Be(0);
    }

    [Test]
    public void LiveAnalysis_BinOnly_ComputesLowestMedianHighest()
    {
        var now = DateTime.UtcNow;
        var samples = new List<PricesService.LiveListingSample>
        {
            Listing(300, true, now, sellerId: 1),
            Listing(100, true, now, sellerId: 2),
            Listing(50, true, now, sellerId: 3),
            Listing(200, true, now, sellerId: 4),
            Listing(150, true, now, sellerId: 5),
        };

        var result = PricesService.ComputeLiveMarketAnalysis(samples, now);

        result.LowestBin.Should().Be(50);
        result.HighestBin.Should().Be(300);
        result.MedianBin.Should().Be(150);
        result.BinCount.Should().Be(5);
    }

    [Test]
    public void LiveAnalysis_NonBinAuctions_ExcludedFromPriceStats_ButCounted()
    {
        var now = DateTime.UtcNow;
        var samples = new List<PricesService.LiveListingSample>
        {
            Listing(10, true, now, sellerId: 1),
            Listing(20, true, now, sellerId: 2),
            Listing(30, true, now, sellerId: 3),
            Listing(999999, false, now, sellerId: 4), // non-bin: must not affect price stats
            Listing(1, false, now, sellerId: 5),
        };

        var result = PricesService.ComputeLiveMarketAnalysis(samples, now);

        result.BinCount.Should().Be(3);
        result.AuctionCount.Should().Be(2);
        result.LowestBin.Should().Be(10);
        result.HighestBin.Should().Be(30);
        result.MedianBin.Should().Be(20);
        result.SellerCount.Should().Be(5);
        result.PriceBuckets.Sum(b => b.Count).Should().Be(3);
    }

    [Test]
    public void LiveAnalysis_P95Trimming_ExcludesOutlier_WhenAtLeastTwentyListings()
    {
        var now = DateTime.UtcNow;
        // 19 listings priced 1..19 plus one extreme outlier at 1000 = 20 total.
        var samples = Enumerable.Range(1, 19)
            .Select(p => Listing(p, true, now, sellerId: p))
            .Append(Listing(1000, true, now, sellerId: 100))
            .ToList();

        var result = PricesService.ComputeLiveMarketAnalysis(samples, now);

        result.BinCount.Should().Be(20);
        result.HighestBin.Should().Be(1000); // untrimmed overall stat
        result.BucketRangeMax.Should().Be(19); // nearest-rank P95 of 20 sorted values -> 19th smallest
        result.AboveRangeCount.Should().Be(1);
        result.PriceBuckets.Sum(b => b.Count).Should().Be(19);
        result.TimeOnMarketBuckets.Sum(b => b.SampleCount).Should().Be(19);
    }

    [Test]
    public void LiveAnalysis_NoTrimming_WhenFewerThanTwentyListings()
    {
        var now = DateTime.UtcNow;
        // 19 listings (below the 20-listing trimming threshold), including an extreme outlier.
        var samples = Enumerable.Range(1, 18)
            .Select(p => Listing(p, true, now, sellerId: p))
            .Append(Listing(1000, true, now, sellerId: 100))
            .ToList();

        var result = PricesService.ComputeLiveMarketAnalysis(samples, now);

        result.BinCount.Should().Be(19);
        result.BucketRangeMax.Should().Be(result.HighestBin);
        result.BucketRangeMax.Should().Be(1000);
        result.AboveRangeCount.Should().Be(0);
        result.PriceBuckets.Sum(b => b.Count).Should().Be(19);
    }

    [Test]
    public void LiveAnalysis_TimeOnMarket_CategorisesFreshAgingStale()
    {
        var now = DateTime.UtcNow;
        var samples = new List<PricesService.LiveListingSample>
        {
            Listing(50, true, now.AddSeconds(-1800), sellerId: 1),   // 30 min -> FRESH
            Listing(250, true, now.AddSeconds(-7200), sellerId: 2),  // 2h -> AGING
            Listing(650, true, now.AddSeconds(-36000), sellerId: 3), // 10h -> STALE
        };

        var result = PricesService.ComputeLiveMarketAnalysis(samples, now);

        result.BucketRangeMax.Should().Be(650); // fewer than 20 listings -> no trimming
        var fresh = result.TimeOnMarketBuckets.Single(b => b.SpeedCategory == "FRESH");
        fresh.SampleCount.Should().Be(1);
        fresh.AvgSellTimeSeconds.Should().BeApproximately(1800, 0.01);

        var aging = result.TimeOnMarketBuckets.Single(b => b.SpeedCategory == "AGING");
        aging.SampleCount.Should().Be(1);
        aging.AvgSellTimeSeconds.Should().BeApproximately(7200, 0.01);

        var stale = result.TimeOnMarketBuckets.Single(b => b.SpeedCategory == "STALE");
        stale.SampleCount.Should().Be(1);
        stale.AvgSellTimeSeconds.Should().BeApproximately(36000, 0.01);
    }

    [Test]
    public void LiveAnalysis_SellerCount_CountsDistinctSellersAcrossBinAndAuctions()
    {
        var now = DateTime.UtcNow;
        var samples = new List<PricesService.LiveListingSample>
        {
            Listing(10, true, now, sellerId: 1),
            Listing(20, true, now, sellerId: 1), // same seller again
            Listing(30, false, now, sellerId: 2),
            Listing(40, false, now, sellerId: 3),
            Listing(50, false, now, sellerId: 0), // unknown seller, excluded
        };

        var result = PricesService.ComputeLiveMarketAnalysis(samples, now);

        result.SellerCount.Should().Be(3);
    }

    [Test]
    public void LiveAnalysis_AllSamePrice_DoesNotThrowEvenWithTrimmingEligibleCount()
    {
        var now = DateTime.UtcNow;
        var samples = Enumerable.Range(0, 25)
            .Select(i => Listing(100, true, now.AddSeconds(-i), sellerId: i + 1))
            .ToList();

        Action act = () => PricesService.ComputeLiveMarketAnalysis(samples, now);

        act.Should().NotThrow();
        var result = PricesService.ComputeLiveMarketAnalysis(samples, now);
        result.LowestBin.Should().Be(100);
        result.HighestBin.Should().Be(100);
        result.BucketRangeMax.Should().Be(100);
        result.AboveRangeCount.Should().Be(0);
        result.PriceBuckets.Should().HaveCount(1);
        result.PriceBuckets.Single().Count.Should().Be(25);
    }
}
