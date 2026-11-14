using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Filter;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class FlipFilterArmorSetTests
{
    private Func<HypixelItemService, Task> originalLoad;
    private int[] loadCalls;

    [SetUp]
    public void Setup()
    {
        originalLoad = ArmorSetDetailedFlipFilter.StartItemLoad;
        // per-test counter so a late call of a previous test's warm-up cannot be counted
        var calls = loadCalls = new int[1];
        // never touch the real loader (http / items.json / endless retry loop)
        ArmorSetDetailedFlipFilter.StartItemLoad = _ =>
        {
            Interlocked.Increment(ref calls[0]);
            return Task.CompletedTask;
        };
        ArmorSetDetailedFlipFilter.ResetWarmUp();
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(new Moq.Mock<INBT>().Object));
        UseItemService(null);
    }

    [TearDown]
    public void TearDown()
    {
        ArmorSetDetailedFlipFilter.StartItemLoad = originalLoad;
        ArmorSetDetailedFlipFilter.ResetWarmUp();
    }

    private static void UseItemService(Dictionary<string, Coflnet.Sky.Core.Services.Item> items)
    {
        var service = new HypixelItemService(new HttpClient(), NullLogger<HypixelItemService>.Instance);
        if (items != null)
            typeof(HypixelItemService).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, items);
        DiHandler.OverrideService<HypixelItemService, HypixelItemService>(service);
    }

    [Test]
    public void UnloadedItemsFallBackToNameMatchingWithoutBlocking()
    {
        var watch = Stopwatch.StartNew();
        var filter = new FlipFilter(new Dictionary<string, string> { { "ArmorSet", "DIVAN" } }, null);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

        filter.IsMatch(Flip("DIVAN_CHESTPLATE")).Should().BeTrue();
        filter.IsMatch(Flip("ASPECT_OF_THE_END")).Should().BeFalse();
    }

    [Test]
    public void OptionsEmptyWhenUnloaded()
    {
        new ArmorSetDetailedFlipFilter().Options.Should().BeEmpty();
    }

    [Test]
    public async Task WarmUpStartsOnlyOnceAcrossConstructions()
    {
        var settings = new Dictionary<string, string> { { "ArmorSet", "DIVAN" } };
        Parallel.For(0, 50, _ => new FlipFilter(settings, null));
        for (var i = 0; i < 10; i++)
            new FlipFilter(settings, null);
        _ = new ArmorSetDetailedFlipFilter().Options;

        var timeout = Stopwatch.StartNew();
        while (Volatile.Read(ref loadCalls[0]) == 0 && timeout.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        await Task.Delay(100);
        loadCalls[0].Should().Be(1);
    }

    [Test]
    public void LoadedItemsUseTheSetDictionary()
    {
        var items = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Coflnet.Sky.Core.Services.Item>>("""
            {"CUSTOM_PIECE":{"id":"CUSTOM_PIECE","museum_data":{"armor_set_donation_xp":{"MYSET":5}}},
             "OTHER_PIECE":{"id":"OTHER_PIECE"}}
            """);
        UseItemService(items);

        var filter = new FlipFilter(new Dictionary<string, string> { { "ArmorSet", "MYSET" } }, null);

        filter.IsMatch(Flip("CUSTOM_PIECE")).Should().BeTrue();
        filter.IsMatch(Flip("MYSET_CHESTPLATE")).Should().BeFalse();
        loadCalls[0].Should().Be(0);
    }

    private static FlipInstance Flip(string tag) => new() { Auction = new SaveAuction { Tag = tag } };
}
