using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class ItemFilterSearchServiceTests
{
    private Mock<IItemsApi> api;
    private ItemFilterSearchService service;
    private static FilterQueryParser parser;

    [OneTimeSetUp]
    public void SetupParser()
    {
        parser = new FilterQueryParser(new FilterEngine(new Mock<INBT>().Object));
    }

    [SetUp]
    public void Setup()
    {
        api = new Mock<IItemsApi>();
        api.Setup(a => a.ItemNamesGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ItemPreview>
        {
            new() { Tag = "HYPERION", Name = "Hyperion" },
            new() { Tag = "ASPECT_OF_THE_END", Name = "Aspect of the End" },
            new() { Tag = "ASPECT_OF_THE_DRAGONS", Name = "Aspect of the Dragons" },
        });
        api.Setup(a => a.ItemItemTagGetAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tag, bool? _, int _, CancellationToken _) => new Items.Client.Model.Item
            {
                Tag = tag,
                Name = tag == "HYPERION" ? "Hyperion" : tag == "ASPECT_OF_THE_END" ? "Aspect of the End" : "Aspect of the Dragons",
                Flags = ItemFlags.AUCTION,
                Tier = Items.Client.Model.Tier.LEGENDARY
            });
        service = new ItemFilterSearchService(api.Object, parser, NullLogger<ItemFilterSearchService>.Instance);
    }

    private void VerifyNoSearch() => api.Verify(a => a.ItemsSearchTermGetAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

    [Test]
    public async Task ItemWithFilterReturnsFilterFirstWithoutSearchCall()
    {
        var result = (await service.TryParseAsync("hyperion sharp 7", default)).Results;
        result.Select(r => r.Type).Should().Equal("filter", "item");
        result[0].Id.Should().Be("HYPERION?sharpness=7");
        result[0].Name.Should().Be("Hyperion · Sharpness 7");
        result[0].IconUrl.Should().Be("https://sky.coflnet.com/static/icon/HYPERION");
        result[0].HitCount.Should().Be(50);
        result[1].Id.Should().Be("HYPERION");
        result[1].Name.Should().Be("Hyperion");
        VerifyNoSearch();
    }

    [Test]
    public async Task TypingStillOffersThePlainItem()
    {
        var result = (await service.TryParseAsync("hyperion s", default)).Results;
        result.Should().ContainSingle().Which.Id.Should().Be("HYPERION");
    }

    [Test]
    public async Task LongestPrefixWins()
    {
        var result = (await service.TryParseAsync("aspect of the end sharp 5", default)).Results;
        result[0].Id.Should().Be("ASPECT_OF_THE_END?sharpness=5");
        result[1].Id.Should().Be("ASPECT_OF_THE_END");
    }

    [Test]
    public async Task AliasFallsBackToItemSearch()
    {
        api.Setup(a => a.ItemsSearchTermGetAsync("aote", 5, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult> { new() { Tag = "ASPECT_OF_THE_END", Text = "Aspect of the End", Flags = ItemFlags.AUCTION, Tier = Items.Client.Model.Tier.RARE } });
        api.Setup(a => a.ItemsSearchTermGetAsync("aote sharp", 5, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());
        var result = (await service.TryParseAsync("aote sharp 5", default)).Results;
        result.Select(r => r.Id).Should().Equal("ASPECT_OF_THE_END?sharpness=5", "ASPECT_OF_THE_END");
        api.Verify(a => a.ItemsSearchTermGetAsync("aote", 5, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AliasPrefersTheAuctionItemOverBazaarOnly()
    {
        api.Setup(a => a.ItemsSearchTermGetAsync("aote", 5, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>
            {
                new() { Tag = "AOTE_STONE", Text = "Stone", Flags = ItemFlags.BAZAAR | ItemFlags.AUCTION, Tier = Items.Client.Model.Tier.RARE },
                new() { Tag = "ASPECT_OF_THE_END", Text = "Aspect of the End", Flags = ItemFlags.AUCTION, Tier = Items.Client.Model.Tier.RARE }
            });
        api.Setup(a => a.ItemsSearchTermGetAsync("aote sharp", 5, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());
        var result = (await service.TryParseAsync("aote sharp 5", default)).Results;
        result.Select(r => r.Id).Should().Equal("ASPECT_OF_THE_END?sharpness=5", "ASPECT_OF_THE_END");
    }

    [Test]
    public async Task FilterResultUsesTheItemTierNotTheShiftedSearchTier()
    {
        var result = (await service.TryParseAsync("hyperion sharp 7", default)).Results;
        result.Select(r => r.Tier).Should().AllBeEquivalentTo(Core.Tier.LEGENDARY);
    }

    [Test]
    public async Task ReportsTimeoutWhenTheBudgetIsExceeded()
    {
        api.Setup(a => a.ItemNamesGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { await Task.Delay(3000); return new List<ItemPreview>(); });
        var slow = new ItemFilterSearchService(api.Object, parser, NullLogger<ItemFilterSearchService>.Instance);
        var result = await slow.TryParseAsync("hyperion sharp 7", default);
        result.TimedOut.Should().BeTrue();
        result.Results.Should().BeEmpty();
    }

    /// <summary>
    /// Regression: the generated client wraps the budget cancellation in an ApiException,
    /// which was treated as a failed lookup and returned a cacheable empty answer
    /// </summary>
    [Test]
    public async Task ApiExceptionAfterCancellationCountsAsTimeout()
    {
        api.Setup(a => a.ItemsSearchTermGetAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, int? _, int _, CancellationToken ct) =>
            {
                await Task.Delay(3000, CancellationToken.None);
                throw new Items.Client.Client.ApiException(0, "Error calling ItemsSearchTermGet: The operation was canceled.");
            });
        var result = await service.TryParseAsync("aote sharp 5", default);
        result.TimedOut.Should().BeTrue();
        result.Results.Should().BeEmpty();
    }

    [Test]
    public async Task NormalAnswersAreNotMarkedTimedOut()
    {
        (await service.TryParseAsync("hyperion sharp 7", default)).TimedOut.Should().BeFalse();
        (await service.TryParseAsync("nothing here", default)).TimedOut.Should().BeFalse();
    }

    [Test]
    public async Task PossessiveNamesMatchWithoutApostrophe()
    {
        api.Setup(a => a.ItemNamesGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ItemPreview>
        {
            new() { Tag = "NECRON_CHESTPLATE", Name = "Necron's Chestplate" },
            new() { Tag = "NECRON_HANDLE", Name = "Necron's Handle" },
            new() { Tag = "HYPERION", Name = "Hyperion" },
        });
        var fresh = new ItemFilterSearchService(api.Object, parser, NullLogger<ItemFilterSearchService>.Instance);
        var result = (await fresh.TryParseAsync("necron chestplate 5 stars recomb", default)).Results;
        result.Select(r => r.Id).Should().Equal("NECRON_CHESTPLATE?Stars=5&Recombobulated=true", "NECRON_CHESTPLATE");
        VerifyNoSearch();
    }

    [Test]
    public async Task SlowDetailsLookupStillAnswersWithUnknownTier()
    {
        api.Setup(a => a.ItemItemTagGetAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { await Task.Delay(3000); return null; });
        var result = await service.TryParseAsync("hyperion sharp 7", default);
        result.TimedOut.Should().BeFalse();
        result.Results.Select(r => r.Type).Should().Equal("filter", "item");
        result.Results.Select(r => r.Tier).Should().AllBeEquivalentTo(Core.Tier.UNKNOWN);
    }

    [Test]
    public async Task UnknownItemReturnsEmpty()
    {
        api.Setup(a => a.ItemsSearchTermGetAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());
        (await service.TryParseAsync("zzz sharp 5", default)).Results.Should().BeEmpty();
    }

    [TestCase("hyperion")]
    [TestCase("hyperion sharp 7 heroic recomb 5 stars extra words here")]
    public async Task WrongWordCountReturnsEmpty(string query)
    {
        (await service.TryParseAsync(query, default)).Results.Should().BeEmpty();
        VerifyNoSearch();
        api.Verify(a => a.ItemItemTagGetAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Regression: SkyApi crashed on startup because the service needed an IMemoryCache that nothing registered.
    /// The service owns a private bounded cache now, and the shared setup must not register a memory cache for other services.
    /// </summary>
    [Test]
    public void ServiceResolvesWithoutASharedMemoryCache()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { { "ITEMS_BASE_URL", "http://localhost:5014" } }).Build());
        services.AddLogging();
        services.AddCoflService();
        services.AddSingleton<ItemFilterSearchService>();
        using var provider = services.BuildServiceProvider();

        provider.GetService<IMemoryCache>().Should().BeNull();
        provider.GetRequiredService<ItemFilterSearchService>().Should().NotBeNull();
    }

    [Test]
    public async Task CacheNeverGrowsBeyondItsLimit()
    {
        api.Setup(a => a.ItemsSearchTermGetAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string term, int? _, int _, CancellationToken _) => new List<SearchResult>
            {
                new() { Tag = "X_" + term.Length, Text = term, Flags = ItemFlags.AUCTION, Tier = Items.Client.Model.Tier.RARE }
            });
        using var small = new ItemFilterSearchService(api.Object, parser, NullLogger<ItemFilterSearchService>.Instance, 5);
        for (var i = 0; i < 40; i++)
            await small.TryParseAsync($"zz{i} qq{i} sharp 5", default);
        small.CachedEntries.Should().BeLessThanOrEqualTo(5);
    }

    [Test]
    public async Task OverlongPrefixIsNeitherLookedUpNorCached()
    {
        var longWord = new string('a', ItemFilterSearchService.MaxPrefixLength + 1);
        using var fresh = new ItemFilterSearchService(api.Object, parser, NullLogger<ItemFilterSearchService>.Instance);
        (await fresh.TryParseAsync($"{longWord} sharp 5", default)).Results.Should().BeEmpty();
        api.Verify(a => a.ItemsSearchTermGetAsync(longWord, It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        fresh.CachedEntries.Should().Be(0);
    }

    [Test]
    public void MissesAreCachedShorterThanHits()
    {
        ItemFilterSearchService.LookupLifetime(false).Should().BeLessThanOrEqualTo(System.TimeSpan.FromMinutes(5));
        ItemFilterSearchService.LookupLifetime(false).Should().BeLessThan(ItemFilterSearchService.LookupLifetime(true));
    }

    [Test]
    public async Task RepeatedMissCallsTheItemSearchOnlyOnce()
    {
        api.Setup(a => a.ItemsSearchTermGetAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());
        await service.TryParseAsync("unknownthing sharp 5", default);
        await service.TryParseAsync("unknownthing sharp 5", default);
        api.Verify(a => a.ItemsSearchTermGetAsync("unknownthing", 5, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
