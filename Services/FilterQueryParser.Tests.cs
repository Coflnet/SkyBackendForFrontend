using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using AwesomeAssertions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class FilterQueryParserTests
{
    private FilterQueryParser parser;

    [OneTimeSetUp]
    public void Setup()
    {
        parser = new FilterQueryParser(new FilterEngine(new Mock<INBT>().Object));
    }

    private ParsedFilters Parse(string text) => parser.Parse(text.Split(' '));

    [TestCase("sharp 7", "sharpness=7")]
    [TestCase("sharpness vii", "sharpness=7")]
    [TestCase("ult wise 5", "ultimate_wise=5")]
    [TestCase("wise 5", "ultimate_wise=5")]
    [TestCase("gk 7", "giant_killer=7")]
    [TestCase("soul eater 5", "ultimate_soul_eater=5")]
    [TestCase("heroic", "Reforge=Heroic")]
    [TestCase("mythic", "Rarity=MYTHIC")]
    [TestCase("5 stars", "Stars=5")]
    [TestCase("stars 5", "Stars=5")]
    [TestCase("recomb", "Recombobulated=true")]
    [TestCase("hpb 15", "HotPotatoCount=15")]
    [TestCase("hpb", "HotPotatoCount=10")]
    [TestCase("fpb", "HotPotatoCount=15")]
    [TestCase("lvl 100", "PetLevel=100")]
    [TestCase("lvl100", "PetLevel=100")]
    [TestCase("100 lvl", "PetLevel=100")]
    [TestCase("hot potato 10", "HotPotatoCount=10")]
    [TestCase("sharp", "sharpness=>0")]
    [TestCase("sharp max", "sharpness=10")] // max is unknown without modifier data
    [TestCase("clean", "Clean=yes")]
    [TestCase("withered", "Reforge=withered")]
    public void ParsesSingleFilter(string text, string expected)
    {
        var parsed = Parse(text);
        parsed.Unmatched.Should().BeEmpty();
        var (key, value) = (expected.Split('=', 2)[0], expected.Split('=', 2)[1]);
        parsed.Filters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(key, value));
    }

    [Test]
    public void ParsesMultipleFilters()
    {
        var parsed = Parse("sharp 7 heroic recomb");
        parsed.Filters.Should().Equal(new Dictionary<string, string>
        {
            ["sharpness"] = "7",
            ["Reforge"] = "Heroic",
            ["Recombobulated"] = "true"
        });
        parsed.Description.Should().Be("Sharpness 7, Heroic, Recombobulated");
    }

    [Test]
    public void UnknownWordIsUnmatchedAndParsingContinues()
    {
        var parsed = Parse("foo sharp 7");
        parsed.Unmatched.Should().Equal("foo");
        parsed.Filters.Should().ContainKey("sharpness").WhoseValue.Should().Be("7");
    }

    [Test]
    public void FormatIdEscapesValues()
    {
        FilterQueryParser.FormatId("HYPERION", new Dictionary<string, string> { ["sharpness"] = ">5", ["Reforge"] = "a b" })
            .Should().Be("HYPERION?sharpness=%3E5&Reforge=a%20b");
        FilterQueryParser.FormatId("HYPERION", new Dictionary<string, string>()).Should().Be("HYPERION");
    }

    [Test]
    public void SuggestListsNamesAndAliases()
    {
        parser.Suggest("sha", 10).Should().Contain("sharpness");
        parser.Suggest("recom", 10).Should().Contain("recomb");
        parser.Suggest("myth", 10).Select(s => s.ToLower()).Should().Contain("mythic");
        parser.Suggest("sha", 1).Should().HaveCount(1);
    }

    [Test]
    public void EveryAliasTargetExists()
    {
        var names = new FilterEngine(new Mock<INBT>().Object).AvailableFilters.Select(f => f.Name).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        FilterQueryParser.Aliases.Values.Select(a => a.Filter).Where(f => !names.Contains(f)).Should().BeEmpty();
    }

    private static readonly HashSet<string> NameWords = new()
    {
        "ender", "enderman", "pet", "superior", "wither", "skeleton", "dragon", "golden", "wise", "strong", "heavy", "perfect", "star", "power", "magma", "armor"
    };

    private ParsedFilters Search(string text)
        => parser.Parse(text.Split(' '), new FilterParseOptions(NameWords.Contains));

    [TestCase("ender")]
    [TestCase("superior")]
    [TestCase("wither")]
    [TestCase("pet")]
    [TestCase("dragon")]
    [TestCase("wise")]
    [TestCase("star")]
    [TestCase("power")]
    [TestCase("perfect")]
    [TestCase("magma")]
    [TestCase("enderman pet")]
    [TestCase("golden dragon")]
    [TestCase("wither skeleton")]
    [TestCase("ender armor")]
    public void SearchModeKeepsNameWordsOutOfFilters(string text)
    {
        var parsed = Search(text);
        parsed.Filters.Should().BeEmpty();
        parsed.Unmatched.Should().Equal(text.Split(' '));
    }

    [Test]
    public void DefaultModeStillTurnsEnderIntoEnchant()
    {
        Parse("ender").Filters.Should().ContainSingle().Which.Key.Should().Be("ender_slayer");
        Parse("wither").Filters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Reforge", "withered"));
    }

    [TestCase("ender slayer 7", "ender_slayer=7")]
    [TestCase("ender 7", "ender_slayer=7")]
    [TestCase("ultimate wise 5", "ultimate_wise=5")]
    [TestCase("wise 5", "ultimate_wise=5")]
    [TestCase("sharpness", "sharpness=>0")]
    [TestCase("sharpness 7", "sharpness=7")]
    [TestCase("withered", "Reforge=withered")]
    [TestCase("legendary", "Rarity=LEGENDARY")]
    [TestCase("ultimate", "Rarity=ULTIMATE")]
    [TestCase("recomb", "Recombobulated=true")]
    [TestCase("hpb 10", "HotPotatoCount=10")]
    [TestCase("stars 5", "Stars=5")]
    [TestCase("reforge heavy", "Reforge=Heavy")]
    public void SearchModeKeepsUnambiguousFilters(string text, string expected)
    {
        var parsed = Search(text);
        parsed.Unmatched.Should().BeEmpty();
        var (key, value) = (expected.Split('=', 2)[0], expected.Split('=', 2)[1]);
        parsed.Filters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(key, value));
    }

    [Test]
    public void SearchModeUltimateWithoutLevelIsRarityAndNameWord()
    {
        var parsed = Search("ultimate wise");
        parsed.Filters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Rarity", "ULTIMATE"));
        parsed.Unmatched.Should().Equal("wise");
    }

    [Test]
    public void SearchModeKeepsRarityAndRichReforge()
    {
        var parsed = Search("rich mythic");
        parsed.Unmatched.Should().BeEmpty();
        parsed.Filters.Should().Contain(new KeyValuePair<string, string>("Reforge", "Rich"))
            .And.Contain(new KeyValuePair<string, string>("Rarity", "MYTHIC"));
    }
}
