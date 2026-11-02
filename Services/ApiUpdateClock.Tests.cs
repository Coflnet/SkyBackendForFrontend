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
        clock.Set(Last);
    }

    [Test]
    public void UnknownBeforeFirstSet()
    {
        Assert.That(new ApiUpdateClock().FirstUpdateAfter(Last), Is.Null);
    }

    [Test]
    public void TimeEqualToLastUpdateReturnsLastUpdate()
    {
        Assert.That(clock.FirstUpdateAfter(Last), Is.EqualTo(Last));
    }

    [Test]
    public void TimeJustAfterReturnsNextMinute()
    {
        Assert.That(clock.FirstUpdateAfter(Last.AddMilliseconds(1)), Is.EqualTo(Last.AddMinutes(1)));
    }

    [Test]
    public void TimeSeveralMinutesLater()
    {
        Assert.That(clock.FirstUpdateAfter(Last.AddMinutes(5).AddSeconds(10)), Is.EqualTo(Last.AddMinutes(6)));
    }

    [TestCase(15, true)]
    [TestCase(21, true)]
    [TestCase(22, false)]
    [TestCase(40, false)]
    public void PublicBedWhenUpdateLandsInsideBedWindow(int updateAfterStartSeconds, bool expected)
    {
        var start = Last.AddMinutes(10).AddSeconds(60 - updateAfterStartSeconds);
        // the next update lands updateAfterStartSeconds after start
        var auction = new SaveAuction { Start = start };
        Assert.That(clock.IsPublicBed(auction, TimeSpan.FromSeconds(1)), Is.EqualTo(expected));
    }

    [Test]
    public void NotPublicBedWhenUnknown()
    {
        Assert.That(new ApiUpdateClock().IsPublicBed(new SaveAuction { Start = Last }, TimeSpan.FromSeconds(1)), Is.False);
    }
}
