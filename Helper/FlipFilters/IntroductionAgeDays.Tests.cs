using System.Collections.Generic;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class IntroductionAgeDaysTests
{
    [Test]
    public void ShouldNotShowNewItem()
    {
        var mock = new Mock<IItemsApi>();
        DiHandler.OverrideService<FilterStateService, FilterStateService>(new FilterStateService(
            NullLogger<FilterStateService>.Instance, new Mock<Mayor.Client.Api.IMayorApiApi>().Object, mock.Object));
        mock.Setup(x => x.ItemsRecentGetAsync(1,0, default)).ReturnsAsync(new List<string>() { "different" });

        DiHandler.OverrideService<ItemDetails, ItemDetails>(new ItemDetails(null)
        {
            TagLookup = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(
            new Dictionary<string, int>() {
                { "different", 1 },
                { "diff2", 1 },
                { "diff3", 3},
                { "diff4", 4},
                { "diff5", 5},
                { "diff6", 6},
                { "diff7", 7},
                { "diff8", 8},
                { "diff9", 9},
                { "diff10", 10},
                { "diff11", 11}
            })
        });
        var filter = new IntroductionAgeDaysDetailedFlipFilter();
        var comparer = filter.GetExpression(new(new(), null), "1").Compile();
        Assert.That(comparer, Is.Not.Null);
        var flipSample = new FlipInstance() { Auction = new Core.SaveAuction() { Tag = "test" } };
        // adding new item now does not change
        DiHandler.GetService<ItemDetails>().TagLookup.TryAdd("test", 1);
        Assert.That(comparer(flipSample));
    }

    [Test]
    public async Task StateIsCopiedNotOverriden()
    {
        var filterStateService = new FilterStateService(NullLogger<FilterStateService>.Instance, new Mock<Mayor.Client.Api.IMayorApiApi>().Object, new Mock<IItemsApi>().Object);
        var previousReference = filterStateService.State.CurrentPerks;
        await filterStateService.UpdateState(CreateState());
        var previousIntroRef = filterStateService.State.IntroductionAge[1];
        await filterStateService.UpdateState(CreateState());
        filterStateService.State.IntroductionAge[1].Should().Contain("test");
        filterStateService.State.CurrentPerks.Should().Contain("test");
        filterStateService.State.IntroductionAge[1].Should().BeSameAs(previousIntroRef);
        Assert.That(filterStateService.State.CurrentPerks, Is.SameAs(previousReference));
    }

    private static FilterStateService.FilterState CreateState()
    {
        return new FilterStateService.FilterState()
        {
            IntroductionAge = new Dictionary<int, HashSet<string>>()
            {
                { 1, new HashSet<string>() { "test" } }
            },
            CurrentPerks = new HashSet<string>() { "test" }
        };
    }
}