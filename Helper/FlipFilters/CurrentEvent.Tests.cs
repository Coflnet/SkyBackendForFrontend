
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using static Coflnet.Sky.Commands.Shared.CurrentEventDetailedFlipFilter;

namespace Coflnet.Sky.Commands.Shared;

public class CurrentEventTests
{
    private class StaticTimeFilter : CurrentEventDetailedFlipFilter
    {
        public DateTime Time { get; set; } = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        protected override DateTime Now => Time;
    }
    private FilterStateService.FilterState state;
    [SetUp]
    public void Setup()
    {
        state = new FilterStateService.FilterState();
        DiHandler.OverrideService<FilterStateService, FilterStateService>(
            new FilterStateService(NullLogger<FilterStateService>.Instance, null, null, state));
    }
    [Test]
    public void NewYear()
    {
        AssertEvent(new DateTime(2023, 6, 2, 12, 55, 0, DateTimeKind.Utc), Events.NewYear);
    }
    [Test]
    public void TravelingZooWinter()
    {
        AssertEvent(new DateTime(2023, 6, 3, 20, 55, 0, DateTimeKind.Utc), Events.TravelingZoo);
    }
    [Test]
    public void TravelingZooSummer()
    {
        AssertEvent(new DateTime(2023, 6, 1, 6, 55, 0, DateTimeKind.Utc), Events.TravelingZoo, "HarvestFeast");
    }
    [Test]
    public void SpookyFestival()
    {
        AssertEvent(new DateTime(2023, 6, 5, 23, 35, 0, DateTimeKind.Utc), Events.SpookyFestival, "HarvestFeast");
    }
    [Test]
    public void SeasonOfJerry()
    {
        AssertEvent(new DateTime(2023, 6, 2, 11, 15, 1, DateTimeKind.Utc), Events.SeasonOfJerry);
    }
    [Test]
    public void NotSeasonOfJerry()
    {
        AssertEvent(new DateTime(2023, 6, 3, 11, 15, 1, DateTimeKind.Utc), Events.NotSeasonOfJerry);
    }
    [Test]
    public void DarkAuction()
    {
        AssertEvent(new DateTime(2023, 6, 3, 11, 55, 1, DateTimeKind.Utc), Events.DarkAuction);
    }
    [Test]
    public void NotDarkAuction()
    {
        AssertEvent(new DateTime(2023, 6, 3, 11, 54, 1, DateTimeKind.Utc), Events.NotDarkAuction);
    }
    [Test]
    public void JacobsFarmingContest()
    {
        AssertEvent(new DateTime(2023, 6, 3, 11, 25, 1, DateTimeKind.Utc), Events.JacobsFarmingContest);
    }
    [Test]
    public void HarvestFeastSpansAutumnAndOverlapsSpooky()
    {
        var spooky = new DateTime(2023, 6, 5, 23, 35, 0, DateTimeKind.Utc);
        AssertMatch(spooky, "HarvestFeast");
        AssertMatch(spooky, Events.SpookyFestival);
        AssertMatch(spooky, Events.None, expected: false);
        AssertMatch(spooky, "NotHarvestFeast", expected: false);
        // Early Autumn 1st 00:00 until Late Autumn 31st 24:00
        var start = new DateTime(2023, 6, 5, 3, 55, 0, DateTimeKind.Utc);
        var end = start + TimeSpan.FromHours(31);
        AssertMatch(start, "HarvestFeast");
        AssertMatch(start - TimeSpan.FromSeconds(1), "NotHarvestFeast");
        AssertMatch(end - TimeSpan.FromSeconds(1), "HarvestFeast");
        AssertMatch(end + TimeSpan.FromSeconds(1), "NotHarvestFeast");
    }
    [Test]
    public void GrandFeastPerkExtendsHarvestFeastToWholeYear()
    {
        var outsideAutumn = new DateTime(2023, 6, 3, 11, 15, 1, DateTimeKind.Utc);
        AssertMatch(outsideAutumn, Events.None);
        state.CurrentPerks.Add("Grand Feast");
        AssertMatch(outsideAutumn, "HarvestFeast");
        AssertMatch(outsideAutumn, Events.None, expected: false);
    }

    private static void AssertEvent(DateTime start, Events target, string eventBefore = nameof(Events.None))
    {
        AssertMatch(start, target);
        AssertMatch(start + TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1), target);
        AssertMatch(start - TimeSpan.FromSeconds(1), eventBefore);
    }

    private static void AssertMatch(DateTime time, Events value, bool expected = true)
    {
        AssertMatch(time, value.ToString(), expected);
    }

    private static void AssertMatch(DateTime time, string value, bool expected = true)
    {
        var filter = new StaticTimeFilter();
        filter.Time = time;
        var expression = filter.GetExpression(new(new(), null), value);
        var compiled = expression.Compile();
        var flip = new FlipInstance() { Auction = new() };
        Assert.That(compiled(flip), Is.EqualTo(expected), $"Expected {value} to be {expected} for {time}");
    }

}