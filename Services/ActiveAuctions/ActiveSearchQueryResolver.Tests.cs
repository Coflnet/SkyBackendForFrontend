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

    private static readonly string[] Lapis =
        { "LAPIS_ARMOR_HELMET", "LAPIS_ARMOR_CHESTPLATE", "LAPIS_ARMOR_LEGGINGS", "LAPIS_ARMOR_BOOTS" };

    [SetUp]
    public async Task Setup()
    {
        (resolver, armorSets) = await Build(true);
    }

    private static async Task<(ActiveSearchQueryResolver, ArmorSetResolver)> Build(bool withSets)
    {
        var parser = new FilterQueryParser(new FilterEngine(new Mock<INBT>().Object));
        var names = new Dictionary<string, string>
        {
            ["FINAL_DESTINATION_HELMET"] = "Final Destination Helmet",
            ["FINAL_DESTINATION_CHESTPLATE"] = "Final Destination Chestplate",
            ["FINAL_DESTINATION_LEGGINGS"] = "Final Destination Leggings",
            ["FINAL_DESTINATION_BOOTS"] = "Final Destination Boots",
            ["HYPERION"] = "Hyperion",
            ["LAPIS_ARMOR_HELMET"] = "Lapis Armor Helmet",
            ["LAPIS_ARMOR_CHESTPLATE"] = "Lapis Armor Chestplate",
            ["LAPIS_ARMOR_LEGGINGS"] = "Lapis Armor Leggings",
            ["LAPIS_ARMOR_BOOTS"] = "Lapis Armor Boots",
            ["LAPIS_CHEST_PLUS"] = "Lapis Chest+",
            ["ASPECT_OF_THE_END"] = "Aspect of the End",
            ["ASPECT_OF_THE_DRAGONS"] = "Aspect of the Dragons",
        };
        var sets = withSets
            ? new Dictionary<string, HashSet<string>> { ["FINAL_DESTINATION"] = FinalDestination.ToHashSet(), ["LAPIS_ARMOR"] = Lapis.ToHashSet() }
            : new Dictionary<string, HashSet<string>>();
        var armorSets = new ArmorSetResolver(() => sets, _ => Task.FromResult<IReadOnlyDictionary<string, string>>(names));
        await armorSets.EnsureLoadedAsync();

        var search = new Mock<ItemFilterSearchService>(null, parser, NullLogger<ItemFilterSearchService>.Instance, 10) { CallBase = false };
        var byName = names.ToDictionary(n => ItemFilterSearchService.NormalizePhrase(n.Value), n => n);
        search.Setup(s => s.TryExactItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string phrase, CancellationToken _) => byName.TryGetValue(phrase, out var n)
                ? new ResolvedItem(n.Key, n.Value, phrase.Split(' ').Length) : null);
        search.Setup(s => s.ResolveItemAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] words, CancellationToken _) => words.Length > 0 && words[0] == "hyperion"
                ? new ResolvedItem("HYPERION", "Hyperion", 1) : null);
        search.Setup(s => s.FindNameMatchesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string phrase, CancellationToken _) =>
            {
                var prefix = byName.Where(n => n.Key.StartsWith(phrase)).ToList();
                var contains = byName.Count(n => n.Key.Contains(phrase));
                return new ItemNameMatches(prefix.Count, contains,
                    prefix.Count == 1 ? new ResolvedItem(prefix[0].Value.Key, prefix[0].Value.Value, phrase.Split(' ').Length) : null);
            });
        return (new ActiveSearchQueryResolver(search.Object, armorSets, parser), armorSets);
    }

    [Test]
    public async Task SingleWordResolvesToArmorSet()
    {
        var r = await resolver.ResolveAsync("lapis");
        r.Tags.Should().BeEquivalentTo(Lapis);
        r.ArmorSet.Should().Be("LAPIS_ARMOR");
        r.Description.Should().Be("Lapis Armor set");
    }

    [Test]
    public async Task SetNameWithGenericWordResolvesToSet()
    {
        var r = await resolver.ResolveAsync("lapis armor set");
        r.ArmorSet.Should().Be("LAPIS_ARMOR");
    }

    [Test]
    public async Task WithoutSetsAmbiguousWordIsNameContains()
    {
        var (noSets, _) = await Build(false);
        var r = await noSets.ResolveAsync("lapis");
        r.Tags.Should().BeEmpty();
        r.ArmorSet.Should().BeNull();
        r.ItemNameContains.Should().Be("lapis");
        r.Description.Should().Be("Name contains 'lapis'");
    }

    [Test]
    public async Task ExactNameWithPlusIsSingleTag()
    {
        var r = await resolver.ResolveAsync("lapis chest+");
        r.Tags.Should().Equal("LAPIS_CHEST_PLUS");
        r.ItemNameContains.Should().BeNull();
    }

    [Test]
    public async Task AmbiguousPrefixIsNameContains()
    {
        var r = await resolver.ResolveAsync("aspect");
        r.Tags.Should().BeEmpty();
        r.ItemNameContains.Should().Be("aspect");
    }

    [Test]
    public async Task UniqueItemWordIsSingleTag()
    {
        var r = await resolver.ResolveAsync("hyperion");
        r.Tags.Should().Equal("HYPERION");
        r.ItemNameContains.Should().BeNull();
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
