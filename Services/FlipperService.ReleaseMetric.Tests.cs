using System;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class FlipperServiceReleaseMetricTests
{
    private static readonly DateTime Last = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private FlipperService service;

    [SetUp]
    public void Setup()
    {
        service = new FlipperService(null, null);
        service.ApiClock.SetLastUpdated(Last); // next arrival after 12:00:05 is 12:01:07
    }

    private static LowPricedAuction PreApiFlip() => new()
    {
        Auction = new SaveAuction { Uuid = "u", Start = Last.AddSeconds(5), Context = new() { { "pre-api", "true" } } }
    };

    private static (long count, double sum) Read(string tier, string source, string bed)
    {
        var child = FlipperService.releaseRelativeApi.WithLabels(tier, source, bed);
        return (child.Count, child.Sum);
    }

    [Test]
    public void PreApiFlipReleasedBeforeArrivalIsNegativeAtItsTier()
    {
        var before = Read("PREMIUM_PLUS", "preapi", "false");

        service.ObserveReleaseRelativeApi(AccountTier.PREMIUM_PLUS, PreApiFlip(), Last.AddSeconds(62));

        var after = Read("PREMIUM_PLUS", "preapi", "false");
        Assert.That(after.count, Is.EqualTo(before.count + 1));
        Assert.That(after.sum - before.sum, Is.EqualTo(-5d).Within(0.001));
    }

    [Test]
    public void NegativeObservationOnlyIncreasesEarlyCounterAndPositiveOnlyLate()
    {
        var sums = FlipperService.releaseRelativeApiSums;
        var (early, late) = (sums.Early("PREMIUM_PLUS", "preapi"), sums.Late("PREMIUM_PLUS", "preapi"));

        service.ObserveReleaseRelativeApi(AccountTier.PREMIUM_PLUS, PreApiFlip(), Last.AddSeconds(62));
        Assert.That(sums.Early("PREMIUM_PLUS", "preapi"), Is.EqualTo(early + 5).Within(0.001));
        Assert.That(sums.Late("PREMIUM_PLUS", "preapi"), Is.EqualTo(late));

        service.ObserveReleaseRelativeApi(AccountTier.PREMIUM_PLUS, PreApiFlip(), Last.AddSeconds(70));
        Assert.That(sums.Early("PREMIUM_PLUS", "preapi"), Is.EqualTo(early + 5).Within(0.001));
        Assert.That(sums.Late("PREMIUM_PLUS", "preapi"), Is.EqualTo(late + 3).Within(0.001));
    }

    [Test]
    public void ImplausibleAndNullFlipsDoNotTouchTheCounters()
    {
        var sums = FlipperService.releaseRelativeApiSums;
        var (early, late) = (sums.Early("PREMIUM", "preapi"), sums.Late("PREMIUM", "preapi"));

        service.ObserveReleaseRelativeApi(AccountTier.PREMIUM, PreApiFlip(), Last.AddHours(3));
        service.ObserveReleaseRelativeApi(AccountTier.PREMIUM, null, Last.AddSeconds(70));

        Assert.That((sums.Early("PREMIUM", "preapi"), sums.Late("PREMIUM", "preapi")), Is.EqualTo((early, late)));
    }

    [Test]
    public void NullFlipIsSkipped()
    {
        var before = Read("PREMIUM", "preapi", "false");
        service.ObserveReleaseRelativeApi(AccountTier.PREMIUM, null, Last.AddSeconds(62));
        Assert.That(Read("PREMIUM", "preapi", "false").count, Is.EqualTo(before.count));
    }

    [Test]
    public void RustFlipIsNeitherObservedNorCountedAsOutlier()
    {
        var flip = PreApiFlip();
        flip.Finder = LowPricedAuction.FinderType.Rust;
        var outliers = FlipperService.releaseRelativeApiOutliers;
        var before = outliers.WithLabels("NONE", "rust", "early").Value + outliers.WithLabels("NONE", "rust", "late").Value;

        service.ObserveReleaseRelativeApi(AccountTier.NONE, flip, Last.AddHours(5));

        Assert.That(outliers.WithLabels("NONE", "rust", "early").Value + outliers.WithLabels("NONE", "rust", "late").Value, Is.EqualTo(before));
    }

    [Test]
    public void ImplausibleReleaseIsCountedAsOutlier()
    {
        var outliers = FlipperService.releaseRelativeApiOutliers.WithLabels("STARTER_PREMIUM", "preapi", "late");
        var before = outliers.Value;

        service.ObserveReleaseRelativeApi(AccountTier.STARTER_PREMIUM, PreApiFlip(), Last.AddHours(3));

        Assert.That(outliers.Value, Is.EqualTo(before + 1));
    }
}
