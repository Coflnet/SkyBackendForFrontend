using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.FlipTracker.Client.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NUnit.Framework;
using FlipFlags = Coflnet.Sky.Commands.Shared.FlipFlags;
using TrackerFlags = Coflnet.Sky.FlipTracker.Client.Model.FlipFlags;

namespace Coflnet.Sky.Commands;

public class FlipTrackingServiceTests
{
    [TestCase("DifferentBuyer, ViaTrade, UnknownCost", 11)]
    [TestCase("ViaTrade, MultiItemTrade, UncertainCost", 22)]
    public async Task PlayerFlipsDeserializeTradeFlagsBeforeFilteringZeroProfit(string flags, int expectedFlags)
    {
        var player = Guid.NewGuid();
        var response = JsonConvert.SerializeObject(new[]
        {
            new { purchaseAuctionId = Guid.NewGuid(), profit = 100L, flags = "None", profitChanges = Array.Empty<object>() },
            new { purchaseAuctionId = Guid.NewGuid(), profit = -50L, flags, profitChanges = Array.Empty<object>() },
            new { purchaseAuctionId = Guid.NewGuid(), profit = 0L, flags, profitChanges = Array.Empty<object>() }
        });
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var calls = 0;
        app.Run(async context =>
        {
            calls++;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(response);
        });
        await app.StartAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["FLIPTRACKER_BASE_URL"] = app.Urls.Single()
        }).Build();
        var service = new FlipTrackingService(null, null, config, null, null, null, null, null, null, null);

        var result = await service.GetPlayerFlips(player.ToString(), TimeSpan.FromHours(1));

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(result.Flips.Select(f => f.Profit), Is.EqualTo(new long[] { 100, -50 }));
        Assert.That(result.TotalProfit, Is.EqualTo(50));
        Assert.That((int)result.Flips[1].Flags, Is.EqualTo(expectedFlags));
        Assert.That(JsonConvert.SerializeObject(result.Flips[1].Flags), Is.EqualTo(JsonConvert.SerializeObject(flags)));
    }

    [TestCase(1, FlipFlags.DifferentBuyer)]
    [TestCase(2, FlipFlags.ViaTrade)]
    [TestCase(4, FlipFlags.MultiItemTrade)]
    [TestCase(8, FlipFlags.UnknownCost)]
    [TestCase(16, FlipFlags.UncertainCost)]
    [TestCase(31, FlipFlags.DifferentBuyer | FlipFlags.ViaTrade | FlipFlags.MultiItemTrade | FlipFlags.UnknownCost | FlipFlags.UncertainCost)]
    public void ConversionPreservesFlagBits(int flags, FlipFlags expected)
    {
        var flip = new PastFlip { Flags = (TrackerFlags)flags, ProfitChanges = [] };

        Assert.That(FlipTrackingService.Convert(flip).Flags, Is.EqualTo(expected));
        Assert.That((int)expected, Is.EqualTo(flags));
    }
}
