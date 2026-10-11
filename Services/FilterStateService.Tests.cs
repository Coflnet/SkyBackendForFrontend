using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class FilterStateServiceTests
{
    private static List<string> Tags() => Enumerable.Range(0, 400).Select(i => "TAG_" + i).ToList();

    [Test]
    public async Task ParallelCategoryLoadsShareOneCompleteLoad()
    {
        const int workers = 64;
        var calls = 0;
        var joined = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IItemsApi>();
        api.Setup(a => a.ItemsCategoryCategoryItemsGetAsync(ItemCategory.SWORD, 0, default))
            .Returns(async () =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task; // held open until every worker has joined the load
                await Task.Delay(2);
                return Tags();
            });
        var service = new FilterStateService(NullLogger<FilterStateService>.Instance, new Mock<Mayor.Client.Api.IMayorApiApi>().Object, api.Object);

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            var load = service.GetItemCategory(ItemCategory.SWORD);
            Interlocked.Increment(ref joined);
            await load;
            return service.State.itemCategories[ItemCategory.SWORD].Count;
        })).ToArray();
        while (Volatile.Read(ref joined) < workers)
            await Task.Delay(1);
        gate.SetResult();
        var counts = await Task.WhenAll(tasks);

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(counts, Has.All.EqualTo(400));
    }

    [Test]
    public async Task BuiltCategoryFilterSeesRefreshedSet()
    {
        var api = new Mock<IItemsApi>();
        api.Setup(a => a.ItemsCategoryCategoryItemsGetAsync(ItemCategory.SWORD, 0, default))
            .ReturnsAsync(Tags());
        var service = new FilterStateService(NullLogger<FilterStateService>.Instance, new Mock<Mayor.Client.Api.IMayorApiApi>().Object, api.Object);
        DiHandler.OverrideService<FilterStateService, FilterStateService>(service);
        await service.GetItemCategory(ItemCategory.SWORD);

        var filter = new ItemCategoryDetailedFlipFilter().GetExpression(new(new(), null), "SWORD").Compile();
        FlipInstance Flip(string tag) => new() { Auction = new Coflnet.Sky.Core.SaveAuction { Tag = tag } };
        Assert.That(filter(Flip("TAG_1")), Is.True);
        Assert.That(filter(Flip("NEW_TAG")), Is.False);

        var newState = new FilterStateService.FilterState();
        newState.itemCategories[ItemCategory.SWORD] = Tags().Append("NEW_TAG").ToHashSet();
        await service.UpdateState(newState);

        Assert.That(filter(Flip("NEW_TAG")), Is.True);
        Assert.That(filter(Flip("TAG_399")), Is.True);
        Assert.That(filter(Flip("OTHER")), Is.False);
    }

    [Test]
    public async Task FailedCategoryLoadIsRetried()
    {
        var calls = 0;
        var api = new Mock<IItemsApi>();
        api.Setup(a => a.ItemsCategoryCategoryItemsGetAsync(ItemCategory.SWORD, 0, default))
            .Returns(() => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? null : Tags()));
        var service = new FilterStateService(NullLogger<FilterStateService>.Instance, new Mock<Mayor.Client.Api.IMayorApiApi>().Object, api.Object);

        Assert.ThrowsAsync<Coflnet.Sky.Core.CoflnetException>(() => service.GetItemCategory(ItemCategory.SWORD));
        Assert.That(service.State.itemCategories.ContainsKey(ItemCategory.SWORD), Is.False);
        await service.GetItemCategory(ItemCategory.SWORD);

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(service.State.itemCategories[ItemCategory.SWORD].Count, Is.EqualTo(400));
    }
}
