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

    private static readonly string[] EnderSet = { "ENDER_HELMET", "ENDER_CHESTPLATE", "ENDER_LEGGINGS", "ENDER_BOOTS" };
    private static readonly string[] SuperiorSet =
        { "SUPERIOR_DRAGON_HELMET", "SUPERIOR_DRAGON_CHESTPLATE", "SUPERIOR_DRAGON_LEGGINGS", "SUPERIOR_DRAGON_BOOTS" };

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
            ["ENDER_HELMET"] = "Ender Helmet",
            ["ENDER_CHESTPLATE"] = "Ender Chestplate",
            ["ENDER_LEGGINGS"] = "Ender Leggings",
            ["ENDER_BOOTS"] = "Ender Boots",
            ["ENDERMAN_100"] = "[Lvl 1] Enderman",
            ["SUPERIOR_DRAGON_HELMET"] = "Superior Dragon Helmet",
            ["SUPERIOR_DRAGON_CHESTPLATE"] = "Superior Dragon Chestplate",
            ["SUPERIOR_DRAGON_LEGGINGS"] = "Superior Dragon Leggings",
            ["SUPERIOR_DRAGON_BOOTS"] = "Superior Dragon Boots",
            ["WISE_DRAGON_BOOTS"] = "Wise Dragon Boots",
            ["STRONG_DRAGON_BOOTS"] = "Strong Dragon Boots",
            ["HEAVY_HELMET"] = "Heavy Helmet",
            ["PERFECT_HELMET"] = "Perfect Helmet",
        };
        var sets = withSets
            ? new Dictionary<string, HashSet<string>> { ["FINAL_DESTINATION"] = FinalDestination.ToHashSet(), ["LAPIS_ARMOR"] = Lapis.ToHashSet(),
                ["ENDER"] = EnderSet.ToHashSet(), ["SUPERIOR_DRAGON"] = SuperiorSet.ToHashSet() }
            : new Dictionary<string, HashSet<string>>();
        var armorSets = new ArmorSetResolver(() => sets, _ => Task.FromResult<IReadOnlyDictionary<string, string>>(names));
        await armorSets.EnsureLoadedAsync();

        var search = new Mock<ItemFilterSearchService>(null, parser, NullLogger<ItemFilterSearchService>.Instance, 10) { CallBase = false };
        var byName = names.ToDictionary(n => ItemFilterSearchService.NormalizePhrase(n.Value), n => n);
        search.Setup(s => s.TryExactItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string phrase, CancellationToken _) => byName.TryGetValue(phrase, out var n)
                ? new ResolvedItem(n.Key, n.Value, phrase.Split(' ').Length) : null);
        var nameWords = names.Values.SelectMany(n => System.Text.RegularExpressions.Regex.Split(n.ToLowerInvariant(), "[^a-z]+"))
            .Where(w => w.Length >= 2).ToHashSet();
        search.Setup(s => s.GetNameWordsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(nameWords);
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
    public async Task EnderIsNameSearchNotEnchantFilter()
    {
        var r = await resolver.ResolveAsync("ender");
        r.ItemNameContains.Should().Be("ender");
        r.Filters.Should().BeEmpty();
        r.Suggestions.Should().Contain(x => x.Kind == "armorSet" && x.Key == "ENDER")
            .Which.Tags.Should().BeEquivalentTo(EnderSet);
    }

    [Test]
    public async Task SuperiorDragonIsNameSearchWithSetSuggestion()
    {
        var r = await resolver.ResolveAsync("superior dragon");
        r.ItemNameContains.Should().Be("superior dragon");
        r.Filters.Should().BeEmpty();
        r.Suggestions.Should().Contain(x => x.Kind == "armorSet" && x.Key == "SUPERIOR_DRAGON");
    }

    [Test]
    public async Task ExplicitEnchantValueStillFiltersNextToName()
    {
        var r = await resolver.ResolveAsync("ender sharpness 7");
        r.ItemNameContains.Should().Be("ender");
        r.Filters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("sharpness", "7"));
    }

    [Test]
    public async Task EndermanPetIsNameSearch()
    {
        var r = await resolver.ResolveAsync("enderman pet");
        r.ItemNameContains.Should().Be("enderman pet");
        r.Filters.Should().BeEmpty();
    }

    [Test]
    public async Task SingleWordIsNameContainsWithSetSuggestion()
    {
        var r = await resolver.ResolveAsync("lapis");
        r.Tags.Should().BeEmpty();
        r.ArmorSet.Should().BeNull();
        r.ItemNameContains.Should().Be("lapis");
        r.Description.Should().Be("Name contains 'lapis'");
        r.Suggestions.Should().ContainSingle(x => x.Kind == "armorSet" && x.Key == "LAPIS_ARMOR" && x.Label == "Lapis Armor set")
            .Which.Tags.Should().BeEquivalentTo(Lapis);
    }

    [Test]
    public async Task SetWordsAppliesSetWithoutSuggestions()
    {
        foreach (var text in new[] { "lapis armor set", "lapis armor", "final destination set" })
        {
            var r = await resolver.ResolveAsync(text);
            r.ArmorSet.Should().NotBeNull(text);
            r.Tags.Should().NotBeEmpty(text);
            r.ItemNameContains.Should().BeNull(text);
            r.Suggestions.Should().BeEmpty(text);
        }
        (await resolver.ResolveAsync("lapis armor set")).ArmorSet.Should().Be("LAPIS_ARMOR");
        (await resolver.ResolveAsync("lapis armor")).Description.Should().Be("Lapis Armor set");
    }

    [Test]
    public async Task WithoutSetsAmbiguousWordIsNameContains()
    {
        var (noSets, _) = await Build(false);
        var r = await noSets.ResolveAsync("lapis");
        r.Tags.Should().BeEmpty();
        r.ArmorSet.Should().BeNull();
        r.ItemNameContains.Should().Be("lapis");
        r.Suggestions.Should().BeEmpty();
    }

    [Test]
    public async Task ExactNameWithPlusIsContainsWithItemSuggestion()
    {
        var r = await resolver.ResolveAsync("lapis chest+");
        r.Tags.Should().BeEmpty();
        r.ItemNameContains.Should().Be("lapis chest+");
        r.Suggestions.Should().Contain(x => x.Kind == "item" && x.Key == "LAPIS_CHEST_PLUS");
    }

    [Test]
    public async Task AmbiguousPrefixIsNameContains()
    {
        var r = await resolver.ResolveAsync("aspect");
        r.Tags.Should().BeEmpty();
        r.ItemNameContains.Should().Be("aspect");
        r.Suggestions.Should().BeEmpty();
    }

    [Test]
    public async Task ExactItemIsContainsPlusItemSuggestion()
    {
        var r = await resolver.ResolveAsync("hyperion");
        r.Tags.Should().BeEmpty();
        r.ItemNameContains.Should().Be("hyperion");
        r.Suggestions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ActiveQuerySuggestion("item", "HYPERION", "Hyperion", new[] { "HYPERION" }));
    }

    [Test]
    public async Task UniquePrefixIsItemSuggestion()
    {
        var r = await resolver.ResolveAsync("hyperio");
        r.ItemNameContains.Should().Be("hyperio");
        r.Suggestions.Should().ContainSingle(x => x.Key == "HYPERION");
    }

    [Test]
    public async Task SetNameIsContainsWithSetSuggestion()
    {
        var r = await resolver.ResolveAsync("final destination");
        r.Tags.Should().BeEmpty();
        r.ArmorSet.Should().BeNull();
        r.ItemNameContains.Should().Be("final destination");
        r.Suggestions.Should().ContainSingle(x => x.Kind == "armorSet" && x.Key == "FINAL_DESTINATION");
    }

    [Test]
    public async Task ExactSetPieceIsContains()
    {
        var r = await resolver.ResolveAsync("final destination helmet");
        r.Tags.Should().BeEmpty();
        r.ItemNameContains.Should().Be("final destination helmet");
        r.Suggestions.Should().ContainSingle(x => x.Key == "FINAL_DESTINATION_HELMET");
    }

    [Test]
    public async Task NameWithFilterWordsAfterwards()
    {
        var r = await resolver.ResolveAsync("final destination 7 sharpness");
        r.Tags.Should().BeEmpty();
        r.ArmorSet.Should().BeNull();
        r.ItemNameContains.Should().Be("final destination");
        r.Filters.Should().Equal(new Dictionary<string, string> { ["sharpness"] = "7" });
        r.Description.Should().Be("Name contains 'final destination', Sharpness 7");
        r.Suggestions.Should().ContainSingle(x => x.Key == "FINAL_DESTINATION");
    }

    [Test]
    public async Task ItemWithFilter()
    {
        var r = await resolver.ResolveAsync("hyperion sharpness 7");
        r.Tags.Should().BeEmpty();
        r.ItemNameContains.Should().Be("hyperion");
        r.Filters.Should().Equal(new Dictionary<string, string> { ["sharpness"] = "7" });
    }

    [Test]
    public async Task UnknownWordsBecomeNameContains()
    {
        var r = await resolver.ResolveAsync("zzfoo qqbar");
        r.Tags.Should().BeEmpty();
        r.Filters.Should().BeEmpty();
        r.ItemNameContains.Should().Be("zzfoo qqbar");
        r.Suggestions.Should().BeEmpty();
    }

    [Test]
    public async Task FilterOnlyQueryHasNoNameOrSuggestions()
    {
        var r = await resolver.ResolveAsync("sharpness 7");
        r.Tags.Should().BeEmpty();
        r.ItemNameContains.Should().BeNull();
        r.Filters.Should().ContainKey("sharpness");
        r.Suggestions.Should().BeEmpty();
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
