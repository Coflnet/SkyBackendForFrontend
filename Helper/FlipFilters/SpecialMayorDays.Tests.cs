
using System;
using FluentAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class SpecialMayorDaysTests
{
    [Test]
    public void CheckTimeToDerpy()
    {
        var derpyFilter = new DerpyDaysDetailedFlipFilter();
        var nextDerpy = derpyFilter.NextStartInDays(new DateTime(2025, 7, 20, 7, 15, 0));
        nextDerpy.Should().BeInRange(44, 45);
    }
    [Test]
    public void CheckTimeToScorpius()
    {
        var derpyFilter = new ScorpiusDaysDetailedFlipFilter();
        var nextStart = derpyFilter.NextStartInDays(new DateTime(2025, 7, 20, 7, 15, 0));
        nextStart.Should().BeInRange(2, 3);
    }
}