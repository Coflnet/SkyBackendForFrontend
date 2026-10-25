using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class ActiveSearchQueryResolverTests
{
    private static readonly string[] FinalDestination =
        { "FINAL_DESTINATION_HELMET", "FINAL_DESTINATION_CHESTPLATE", "FINAL_DESTINATION_LEGGINGS", "FINAL_DESTINATION_BOOTS" };
    private ActiveSearchQueryResolver resolver;
    private ArmorSetResolver armorSets;

    [SetUp]
    public async Task Setup()
    {
        var parser = new FilterQueryParser(new FilterEngine(new Mock<INBT>().Object));
        var names = new Dictionary<string, string>
        {
            ["FINAL_DESTINATION_HELMET"] = "Final Destination Helmet",
            ["FINAL_DESTINATION_CHESTPLATE"] = "Final Destination Chestplate",
            ["FINAL_DESTINATION_LEGGINGS"] = "Final Destination Leggings",
            ["FINAL_DESTINATION_BOOTS"] = "Final Destination Boots",
            ["HYPERION"] = "Hyperion",
        };
        var sets = new Dictionary<string, HashSet<string>> { ["FINAL_DESTINATION"] = FinalDestination.ToHashSet() };
        armorSets = new ArmorSetResolver(() => sets, _ => Task.FromResult<IReadOnlyDictionary<string, string>>(names));
        await armorSets.EnsureLoadedAsync();

        var search = new Mock<ItemFilterSearchService>(null, parser, NullLogger<ItemFilterSearchService>.Instance, 10) { CallBase = false };
        var byName = names.ToDictionary(n => ItemFilterSearchService.NormalizePhrase(n.Value), n => n);
        search.Setup(s => s.TryExactItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string phrase, CancellationToken _) => byName.TryGetValue(phrase, out var n)
                ? new ResolvedItem(n.Key, n.Value, phrase.Split(' ').Length) : null);
        search.Setup(s => s.ResolveItemAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] words, CancellationToken _) => words.Length > 0 && words[0] == "hyperion"
                ? new ResolvedItem("HYPERION", "Hyperion", 1) : null);
        resolver = new ActiveSearchQueryResolver(search.Object, armorSets, parser);
    }

    [Test]
    public async Task ArmorSetExpandsToAllPieces()
    {
        var r = await resolver.ResolveAsync("final destination");
        r.Tags.Should().BeEquivalentTo(FinalDestination);
        r.ArmorSet.Should().Be("FINAL_DESTINATION");
        r.Filters.Should().BeEmpty();
        r.Description.Should().Be("Final Destination set");
    }

    [Test]
    public async Task ExactItemNameWinsOverSet()
    {
        var r = await resolver.ResolveAsync("final destination helmet");
        r.Tags.Should().Equal("FINAL_DESTINATION_HELMET");
        r.ArmorSet.Should().BeNull();
    }

    [Test]
    public async Task SetWithFilterWordsAfterwards()
    {
        var r = await resolver.ResolveAsync("final destination 7 sharpness");
        r.Tags.Should().BeEquivalentTo(FinalDestination);
        r.ArmorSet.Should().Be("FINAL_DESTINATION");
        r.Filters.Should().Equal(new Dictionary<string, string> { ["sharpness"] = "7" });
        r.Description.Should().Be("Final Destination set, Sharpness 7");
    }

    [Test]
    public async Task ItemWithFilter()
    {
        var r = await resolver.ResolveAsync("hyperion sharpness 7");
        r.Tags.Should().Equal("HYPERION");
        r.Filters.Should().Equal(new Dictionary<string, string> { ["sharpness"] = "7" });
    }

    [Test]
    public async Task UnknownWordsBecomeNameContains()
    {
        var r = await resolver.ResolveAsync("zzfoo qqbar");
        r.Tags.Should().BeEmpty();
        r.Filters.Should().BeEmpty();
        r.ItemNameContains.Should().Be("zzfoo qqbar");
    }

    [Test]
    public async Task FilterOnlyQueryHasNoTags()
    {
        var r = await resolver.ResolveAsync("sharp 7");
        r.Tags.Should().BeEmpty();
        r.Filters.Should().ContainKey("sharpness");
    }

    [Test]
    public void ArmorSetLookupByKeyAndName()
    {
        armorSets.GetTags("final destination").Should().BeEquivalentTo(FinalDestination);
        armorSets.GetTags("FINAL_DESTINATION").Should().BeEquivalentTo(FinalDestination);
        armorSets.GetTags("nope").Should().BeNull();
        armorSets.TryMatch(new[] { "x", "final", "destination" }, 1, out var key, out _, out var consumed).Should().BeTrue();
        (key, consumed).Should().Be(("FINAL_DESTINATION", 2));
    }
}
