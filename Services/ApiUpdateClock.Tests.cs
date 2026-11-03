using System;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Tests;

public class ApiUpdateClockTests
{
    private static readonly DateTime Last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private ApiUpdateClock clock;

    [SetUp]
    public void Setup()
    {
        clock = new ApiUpdateClock();
        clock.SetLastUpdated(Last);
    }

    [Test]
    public void UnknownBeforeFirstSet()
    {
        Assert.That(new ApiUpdateClock().FirstArrivalAfter(Last), Is.Null);
    }

    [Test]
    public void SetSubtractsTheTenSecondsTheApiTimeEndpointAdds()
    {
        var c = new ApiUpdateClock();
        c.Set(Last.AddSeconds(10));
        Assert.That(c.LastUpdate, Is.EqualTo(Last));
    }

    [Test]
    public void GridNeverMovesBackwards()
    {
        clock.SetLastUpdated(Last.AddMinutes(-1));
        Assert.That(clock.LastUpdate, Is.EqualTo(Last));
    }

    [Test]
    public void FallbackOffsetIsSevenSecondsWhenNothingObserved()
    {
        Assert.That(clock.ArrivalOf(Last), Is.EqualTo(Last.AddSeconds(7)));
        Assert.That(clock.FirstArrivalAfter(Last), Is.EqualTo(Last.AddSeconds(7)));
    }

    [Test]
    public void TimeJustAfterReturnsArrivalOfNextMinute()
    {
        Assert.That(clock.FirstArrivalAfter(Last.AddMilliseconds(1)), Is.EqualTo(Last.AddMinutes(1).AddSeconds(7)));
    }

    [Test]
    public void TimeSeveralMinutesLater()
    {
        Assert.That(clock.FirstArrivalAfter(Last.AddMinutes(5).AddSeconds(10)), Is.EqualTo(Last.AddMinutes(6).AddSeconds(7)));
    }

    [Test]
    public void ObserveArrivalKeepsEarliestPerUpdate()
    {
        clock.ObserveArrival(Last, Last.AddSeconds(9));
        clock.ObserveArrival(Last, Last.AddSeconds(6));
        clock.ObserveArrival(Last, Last.AddSeconds(8));
        Assert.That(clock.ArrivalOf(Last), Is.EqualTo(Last.AddSeconds(6)));
    }

    [Test]
    public void ObserveArrivalMovesGridToNewestUpdate()
    {
        clock.ObserveArrival(Last.AddMinutes(3), Last.AddMinutes(3).AddSeconds(5));
        Assert.That(clock.LastUpdate, Is.EqualTo(Last.AddMinutes(3)));
    }

    [Test]
    public void OffsetIsMedianOfPerUpdateOffsets()
    {
        clock.ObserveArrival(Last, Last.AddSeconds(5));
        clock.ObserveArrival(Last.AddMinutes(1), Last.AddMinutes(1).AddSeconds(6));
        clock.ObserveArrival(Last.AddMinutes(2), Last.AddMinutes(2).AddSeconds(20));
        Assert.That(clock.ArrivalOffset, Is.EqualTo(TimeSpan.FromSeconds(6)));
        // unobserved update uses the estimate
        Assert.That(clock.ArrivalOf(Last.AddMinutes(3)), Is.EqualTo(Last.AddMinutes(3).AddSeconds(6)));
    }

    [Test]
    public void ImplausibleOffsetsAreIgnoredForTheEstimate()
    {
        clock.ObserveArrival(Last, Last.AddSeconds(-3));
        clock.ObserveArrival(Last.AddMinutes(1), Last.AddMinutes(1).AddSeconds(45));
        Assert.That(clock.ArrivalOffset, Is.EqualTo(ApiUpdateClock.FallbackArrivalOffset));
    }

    [Test]
    public void OnlyRecentUpdatesAreKept()
    {
        for (var i = 0; i < 12; i++)
            clock.ObserveArrival(Last.AddMinutes(i), Last.AddMinutes(i).AddSeconds(4));
        // the oldest update was forgotten, falls back to the estimate (same value here, so shift an offset to tell apart)
        clock.ObserveArrival(Last.AddMinutes(12), Last.AddMinutes(12).AddSeconds(10));
        Assert.That(clock.ArrivalOf(Last), Is.EqualTo(Last.AddSeconds(4)));
        Assert.That(clock.ArrivalOf(Last.AddMinutes(12)), Is.EqualTo(Last.AddMinutes(12).AddSeconds(10)));
    }

    [Test]
    public void FirstArrivalAfterUsesObservedArrival()
    {
        clock.ObserveArrival(Last.AddMinutes(1), Last.AddMinutes(1).AddSeconds(3));
        Assert.That(clock.FirstArrivalAfter(Last.AddSeconds(30)), Is.EqualTo(Last.AddMinutes(1).AddSeconds(3)));
    }

    private static LowPricedAuction Flip(DateTime start, DateTime findTime, bool preApi = false) => new()
    {
        Auction = new SaveAuction
        {
            Uuid = "abc",
            Start = start,
            FindTime = findTime,
            Context = preApi ? new() { { "pre-api", "true" } } : new()
        }
    };

    [Test]
    public void ApiRelativeForApiFlipIsRelativeToArrivalOfItsUpdate()
    {
        var c = new ApiUpdateClock();
        c.ObserveArrival(Last, Last.AddSeconds(6));

        var result = c.ApiRelative(Flip(Last.AddSeconds(-100), Last), Last.AddSeconds(9));

        Assert.That(result, Is.EqualTo(new ApiRelativeResult("api", 3d, false)));
    }

    [Test]
    public void ApiRelativeForApiFlipUsesFallbackOffsetWithoutObservation()
    {
        var result = new ApiUpdateClock().ApiRelative(Flip(Last.AddSeconds(-100), Last), Last.AddSeconds(10));
        Assert.That(result, Is.EqualTo(new ApiRelativeResult("api", 3d, false)));
    }

    [Test]
    public void ApiRelativeMarksAuctionsStillInBed()
    {
        var notInBed = new ApiUpdateClock().ApiRelative(Flip(Last.AddSeconds(-15), Last), Last.AddSeconds(10));
        var inBed = new ApiUpdateClock().ApiRelative(Flip(Last.AddSeconds(-5), Last), Last.AddSeconds(10));
        Assert.That(notInBed, Is.EqualTo(new ApiRelativeResult("api", 3d, false)));
        Assert.That(inBed, Is.EqualTo(new ApiRelativeResult("api", 3d, true)));
    }

    [Test]
    public void ApiRelativeForPreApiIsNegativeBeforeArrival()
    {
        var start = Last.AddSeconds(5);
        var arrival = clock.FirstArrivalAfter(start).Value;
        Assert.That(arrival, Is.EqualTo(Last.AddSeconds(67)));

        var result = clock.ApiRelative(Flip(start, default, true), arrival.AddSeconds(-5));

        Assert.That(result, Is.EqualTo(new ApiRelativeResult("preapi", -5d, false)));
    }

    [Test]
    public void ApiRelativeForPreApiInBedIsMarked()
    {
        var start = Last.AddSeconds(50); // arrival is 17s after start
        Assert.That(clock.ApiRelative(Flip(start, default, true), start.AddSeconds(20))!.Value.InBed, Is.True);
    }

    [Test]
    public void ApiRelativeOfRecheckFlipIsMeasuredLikePreApi()
    {
        var start = Last.AddSeconds(50); // arrival is 17s after start
        var flip = Flip(start, default);
        flip.Auction.Context["pre-api"] = "recheck";
        var result = clock.ApiRelative(flip, start.AddSeconds(10))!.Value;
        Assert.That(result.Source, Is.EqualTo("preapi"));
        Assert.That(result.Seconds, Is.EqualTo(-7).Within(0.001));
    }

    [Test]
    public void ApiRelativeOfRustFlipIsNull()
    {
        var flip = Flip(Last.AddSeconds(-100), Last);
        flip.Finder = LowPricedAuction.FinderType.Rust;
        Assert.That(clock.ApiRelative(flip, Last.AddSeconds(7)), Is.Null);
    }

    [Test]
    public void FlipSourceClassifiesFlips()
    {
        var recheck = Flip(Last, Last);
        recheck.Auction.Context["pre-api"] = "recheck";
        var rust = Flip(Last, Last);
        rust.Finder = LowPricedAuction.FinderType.Rust;
        Assert.Multiple(() =>
        {
            Assert.That(ApiUpdateClock.FlipSource(Flip(Last, Last)), Is.EqualTo("api"));
            Assert.That(ApiUpdateClock.FlipSource(Flip(Last, Last, true)), Is.EqualTo("preapi"));
            Assert.That(ApiUpdateClock.FlipSource(recheck), Is.EqualTo("preapi"));
            Assert.That(ApiUpdateClock.FlipSource(rust), Is.EqualTo("rust"));
        });
    }

    [Test]
    public void ApiRelativeForPreApiWithUnknownClockIsNull()
    {
        Assert.That(new ApiUpdateClock().ApiRelative(Flip(Last, default, true), DateTime.UtcNow), Is.Null);
    }

    [TestCase(0d, true)]
    [TestCase(120d, true)]
    [TestCase(-120d, true)]
    [TestCase(120.1, false)]
    [TestCase(-120.1, false)]
    [TestCase(-7200d, false)]
    public void PlausibleIsWithinTwoMinutes(double seconds, bool expected)
    {
        Assert.That(ApiUpdateClock.Plausible(seconds), Is.EqualTo(expected));
    }

    [Test]
    public void RelativeBucketsAreAscendingAndCoverTheLimits()
    {
        Assert.That(ApiUpdateClock.RelativeBuckets, Is.Ordered.Ascending);
        ApiUpdateClock.RelativeBuckets[0] = 5;
        Assert.That(ApiUpdateClock.RelativeBuckets[0], Is.EqualTo(-120));
        Assert.That(ApiUpdateClock.RelativeBuckets.First(), Is.EqualTo(-ApiUpdateClock.PlausibleLimitSeconds));
        Assert.That(ApiUpdateClock.RelativeBuckets.Last(), Is.EqualTo(ApiUpdateClock.PlausibleLimitSeconds));
    }
}
