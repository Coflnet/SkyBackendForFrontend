using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Mayor.Client.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class MayorFiltersTests
{
    [Test]
    public void RefreshIsScheduledJustAfterMayorChange()
    {
        var mayorChange = new DateTimeOffset(2024, 8, 26, 7, 15, 0, TimeSpan.Zero);

        Assert.That(FilterStateService.DelayUntilNextMayorUpdate(mayorChange), Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(FilterStateService.DelayUntilNextMayorUpdate(mayorChange.AddSeconds(30)), Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(FilterStateService.DelayUntilNextMayorUpdate(mayorChange.AddMinutes(1)), Is.EqualTo(TimeSpan.FromHours(124)));
    }

    [Test]
    public async Task HostedServiceRefreshesMayorImmediately()
    {
        var mayorApi = new Mock<IMayorApiApi>();
        using var stateService = new FilterStateService(
            NullLogger<FilterStateService>.Instance,
            mayorApi.Object,
            new Mock<IItemsApi>(MockBehavior.Strict).Object);

        await stateService.StartAsync(CancellationToken.None);
        for (var i = 0; i < 100 && mayorApi.Invocations.Count == 0; i++)
            await Task.Delay(10);

        Assert.That(mayorApi.Invocations, Has.Count.EqualTo(1));
        Assert.That(mayorApi.Invocations[0].Method.Name, Is.EqualTo(nameof(IMayorApiApiSync.MayorLastGet)));
        await stateService.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task LastMayorRefreshUpdatesCompiledFilterWithoutLoadingItems()
    {
        var mayorApi = new Mock<IMayorApiApi>();
        mayorApi.As<IMayorApiApiSync>().Setup(api => api.MayorLastGet(It.IsAny<int>())).Returns("Diana");
        mayorApi.As<IMayorApiApiAsync>().Setup(api => api.MayorNextGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Skip unrelated mayor state updates"));
        var itemsApi = new Mock<IItemsApi>(MockBehavior.Strict);
        var stateService = new FilterStateService(
            NullLogger<FilterStateService>.Instance,
            mayorApi.Object,
            itemsApi.Object);
        stateService.State.PreviousMayor = "marina";
        stateService.State.LastUpdate = DateTime.Now;
        stateService.State.IntroductionAge[2] = [];
        DiHandler.OverrideService<FilterStateService, FilterStateService>(stateService);

        var filter = new LastMayorDetailedFlipFilter();
        var matches = filter.GetExpression(new(new(), null), "Diana").Compile();
        Assert.That(matches(new FlipInstance()), Is.False);

        stateService.State.LastUpdate = DateTime.MinValue;
        Assert.That(matches(new FlipInstance()), Is.False);
        mayorApi.As<IMayorApiApiSync>().Verify(api => api.MayorLastGet(It.IsAny<int>()), Times.Never);

        await stateService.UpdateMayorState();

        Assert.That(matches(new FlipInstance()), Is.True);
        mayorApi.As<IMayorApiApiSync>().Verify(api => api.MayorLastGet(It.IsAny<int>()), Times.Once);
        itemsApi.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ProxySyncUpdatesCompiledLastMayorFilter()
    {
        var mayorApi = new Mock<IMayorApiApi>(MockBehavior.Strict);
        var itemsApi = new Mock<IItemsApi>(MockBehavior.Strict);
        var stateService = new FilterStateService(
            NullLogger<FilterStateService>.Instance,
            mayorApi.Object,
            itemsApi.Object);
        stateService.State.PreviousMayor = "marina";
        DiHandler.OverrideService<FilterStateService, FilterStateService>(stateService);
        var matches = new LastMayorDetailedFlipFilter()
            .GetExpression(new(new(), null), "Diana")
            .Compile();

        Assert.That(matches(new FlipInstance()), Is.False);
        await stateService.UpdateState(new FilterStateService.FilterState { PreviousMayor = "diana" });

        Assert.That(matches(new FlipInstance()), Is.True);
        mayorApi.VerifyNoOtherCalls();
        itemsApi.VerifyNoOtherCalls();
    }

    [TestCase("CurrentMayor", "Aatrox", "TWILIGHT_DAGGER")]
    [TestCase("LastMayor", "Diana", "DAEDALUS_AXE")]
    public void MayorForceBlacklistUsesHostStateAndOverridesMatchingWhitelist(string filterName, string mayor, string itemTag)
    {
        DiHandler.OverrideService<FilterEngine, FilterEngine>(new FilterEngine(new Mock<INBT>(MockBehavior.Strict).Object));
        var sharedState = new FilterStateService.FilterState();
        var originalState = CreateStateService(sharedState);
        originalState.State.CurrentMayor = "paul";
        originalState.State.PreviousMayor = "marina";
        DiHandler.OverrideService<FilterStateService, FilterStateService>(originalState);
        var settings = new FlipSettings
        {
            MinProfit = 0,
            MinVolume = 0,
            BlackList =
            [
                new ListEntry
                {
                    ItemTag = itemTag,
                    filter = new() { { "ForceBlacklist", "true" }, { filterName, mayor } }
                }
            ],
            WhiteList =
            [
                new ListEntry
                {
                    ItemTag = itemTag,
                    filter = new() { { "MinProfitPercentage", "25" }, { "MinProfit", "15000000" } }
                }
            ]
        };
        var flip = new FlipInstance
        {
            MedianPrice = 130_000_000,
            Volume = 1,
            Auction = new SaveAuction
            {
                Tag = itemTag,
                StartingBid = 100_000_000,
                FlatenedNBT = new Dictionary<string, string>()
            },
            Context = new Dictionary<string, string>(),
            Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN
        };

        Assert.That(settings.MatchesSettings(flip).Item1, Is.True);

        var hostState = CreateStateService(sharedState);
        hostState.State.CurrentMayor = "aatrox";
        hostState.State.PreviousMayor = "diana";
        DiHandler.OverrideService<FilterStateService, FilterStateService>(hostState);

        Assert.That(settings.MatchesSettings(flip), Is.EqualTo((false, "forced blacklist matched filter for item")));
    }

    private static FilterStateService CreateStateService(FilterStateService.FilterState state)
    {
        return new FilterStateService(
            NullLogger<FilterStateService>.Instance,
            new Mock<IMayorApiApi>(MockBehavior.Strict).Object,
            new Mock<IItemsApi>(MockBehavior.Strict).Object,
            state);
    }
}
