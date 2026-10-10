using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Crafts.Client.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class CollectionNameResolverTests
{
    private const string Json = """
    {"success":true,"collections":{
      "FARMING":{"name":"Farming","items":{"MELON":{"name":"Melon Slice"},"COCOA_BEANS":{"name":"x"},"INK_SACK:3":{"name":"Cocoa Beans"}}},
      "MINING":{"name":"Mining","items":{"SULPHUR_ORE":{"name":"Sulphur"},"ENDER_STONE":{"name":"End Stone"}}},
      "COMBAT":{"name":"Combat","items":{"SULPHUR":{"name":"Gunpowder"}}}
    }}
    """;

    private static ICollectionNameResolver Resolver() => new CollectionNameResolver(() => Task.FromResult(Json));

    private static async Task<bool> Passes(string name, int level, Dictionary<string, int> tiers, ICollectionNameResolver resolver = null)
    {
        var craft = new ProfitableCraft { ReqCollection = name == null ? null : new RequiredCollection { Name = name, Level = level } };
        var collection = new Dictionary<string, ProfileClient.CollectionElem>();
        foreach (var t in tiers)
            collection[t.Key] = new ProfileClient.CollectionElem { tier = t.Value };
        var ids = await ProfileClient.ResolveCollectionIds(new[] { craft }, resolver ?? Resolver());
        return ProfileClient.IsNotLimitedByCollection(collection, craft, ids);
    }

    [Test]
    public async Task DisplayNameResolvesToId()
    {
        (await Passes("Melon Slice", 6, new() { { "MELON", 6 } })).Should().BeTrue();
        (await Passes("Melon Slice", 6, new() { { "MELON", 5 } })).Should().BeFalse();
        (await Passes("End Stone", 3, new() { { "ENDER_STONE", 3 } })).Should().BeTrue();
        (await Passes("melon slice", 6, new() { { "MELON", 6 } })).Should().BeTrue();
    }

    [Test]
    public async Task SulphurAndGunpowderAreDifferent()
    {
        (await Passes("Sulphur", 5, new() { { "SULPHUR", 9 } })).Should().BeFalse();
        (await Passes("Sulphur", 5, new() { { "SULPHUR_ORE", 5 } })).Should().BeTrue();
        (await Passes("Gunpowder", 5, new() { { "SULPHUR", 9 } })).Should().BeTrue();
    }

    [Test]
    public async Task VariantIdsAndAliases()
    {
        (await Passes("Cocoa Beans", 2, new() { { "INK_SACK:3", 2 } })).Should().BeTrue();
        (await Passes("Dark Oak Wood", 4, new() { { "LOG_2:1", 4 } })).Should().BeTrue();
        (await Passes("Dark Oak Wood", 4, new() { { "LOG_2:1", 3 } })).Should().BeFalse();
    }

    [Test]
    public async Task NamesTheCraftsDataSpellsDifferently()
    {
        // the resource calls these "Raw Cod" and "Hemovibe"
        (await Passes("Raw Fish", 1, new() { { "RAW_FISH", 1 } })).Should().BeTrue();
        (await Passes("Raw Fish", 2, new() { { "RAW_FISH", 1 } })).Should().BeFalse();
        (await Passes("Hemovibe Rift", 5, new() { { "HEMOVIBE", 5 } })).Should().BeTrue();
    }

    [Test]
    public async Task UnknownNameNotMet()
    {
        (await Passes("Nonexistent", 1, new() { { "MELON", 9 } })).Should().BeFalse();
    }

    [Test]
    public async Task NoRequirementAlwaysPasses()
    {
        (await Passes(null, 0, new())).Should().BeTrue();
    }

    [Test]
    public async Task LoadFailureKeepsGatedCraftsFilteredAndNeverThrows()
    {
        var failing = new CollectionNameResolver(() => throw new System.Exception("down"));
        (await Passes("Melon Slice", 1, new() { { "MELON", 9 } }, failing)).Should().BeFalse();
        // aliases still work without the resource
        (await Passes("Oak Wood", 1, new() { { "LOG", 9 } }, failing)).Should().BeTrue();
    }

    [Test]
    public async Task KeepsLastGoodCopyOnFailure()
    {
        var calls = 0;
        var resolver = new CollectionNameResolver(() => ++calls == 1 ? Task.FromResult(Json) : throw new System.Exception("down"));
        (await resolver.GetCollectionId("Melon Slice")).Should().Be("MELON");
        (await resolver.GetCollectionId("Melon Slice")).Should().Be("MELON");
    }
}
