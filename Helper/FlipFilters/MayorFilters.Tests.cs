using System;
using System.Threading;
using System.Threading.Tasks;
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
    public async Task LastMayorRefreshUpdatesCompiledFilterWithoutLoadingItems()
    {
        var mayorApi = new Mock<IMayorApiApi>();
        mayorApi.Setup(api => api.MayorLastGet(It.IsAny<int>())).Returns("Diana");
        mayorApi.Setup(api => api.MayorNextGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
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
        mayorApi.Verify(api => api.MayorLastGet(It.IsAny<int>()), Times.Never);

        await stateService.UpdateMayorState();

        Assert.That(matches(new FlipInstance()), Is.True);
        mayorApi.Verify(api => api.MayorLastGet(It.IsAny<int>()), Times.Once);
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
}
