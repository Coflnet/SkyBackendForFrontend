using System;
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
}
