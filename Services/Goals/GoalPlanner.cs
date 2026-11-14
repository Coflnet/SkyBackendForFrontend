using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Commands.Shared.Goals;

/// <summary>Builds goal step trees from the crafts acquisition plan.</summary>
public class GoalPlanner(ICraftsApi craftsApi, IForgeApi forgeApi)
{
    public const int MaxDepth = 8;

    public async Task<Goal> Build(StepKind rootKind, string itemTag, long quantity, bool sell,
        ISet<string> bazaarTags, Func<string, string> nameLookup, string source)
    {
        bazaarTags ??= new HashSet<string>();
        GoalStep root;
        if (rootKind == StepKind.Forge)
            root = await BuildForge(itemTag, quantity, bazaarTags, nameLookup);
        else
        {
            var plan = await craftsApi.GetAcquisitionAsync(itemTag, quantity, true);
            var steps = FromPlan(plan, bazaarTags, nameLookup);
            root = steps.FirstOrDefault(s => s.Kind == StepKind.Craft) ?? steps.FirstOrDefault()
                ?? new GoalStep { Kind = StepKind.Craft, ItemTag = itemTag, Quantity = quantity };
        }
        if (sell)
            root = new GoalStep { Kind = StepKind.Sell, ItemTag = itemTag, Quantity = quantity, Children = [root] };
        var name = nameLookup?.Invoke(itemTag);
        if (string.IsNullOrEmpty(name))
            name = itemTag;
        return new Goal
        {
            Title = quantity > 1 ? $"{name} ×{quantity}" : name,
            RootItemTag = itemTag,
            Quantity = quantity,
            Source = source,
            Root = root
        };
    }

    private async Task<GoalStep> BuildForge(string itemTag, long quantity, ISet<string> bazaarTags, Func<string, string> nameLookup)
    {
        var flips = await forgeApi.GetAllForgeAsync();
        var flip = flips?.FirstOrDefault(f => f.CraftData?.ItemId == itemTag)
            ?? throw new ArgumentException("not a forge item");
        var forge = new GoalStep { Kind = StepKind.Forge, ItemTag = itemTag, Quantity = quantity };
        foreach (var ingredient in flip.CraftData.Ingredients ?? new())
        {
            var total = ingredient.Count * quantity;
            var type = ingredient.Type;
            if (type is "bits" or "copper" or "mote")
            {
                forge.Children.Add(new GoalStep
                {
                    Kind = StepKind.Npc,
                    ItemTag = ingredient.ItemId,
                    Quantity = total,
                    Note = $"costs {ingredient.Count} {type}"
                });
                continue;
            }
            var plan = await craftsApi.GetAcquisitionAsync(ingredient.ItemId, total, type == "craft" ? true : null);
            forge.Children.AddRange(FromPlan(plan, bazaarTags, nameLookup));
        }
        forge.Children = Merge(forge.Children);
        return forge;
    }

    /// <summary>Pure mapping of an acquisition plan node to goal steps.</summary>
    public static List<GoalStep> FromPlan(CraftAcquisitionPlan plan, ISet<string> bazaarTags, Func<string, string> nameLookup)
        => Map(plan, bazaarTags ?? new HashSet<string>(), 0);

    private static List<GoalStep> Map(CraftAcquisitionPlan plan, ISet<string> bazaar, int depth)
    {
        var purchases = plan.Purchases ?? new();
        if (string.Equals(plan.Method, "craft", StringComparison.OrdinalIgnoreCase))
        {
            var craft = new GoalStep { Kind = StepKind.Craft, ItemTag = plan.ItemId, Quantity = plan.Quantity };
            if (depth < MaxDepth)
            {
                foreach (var ingredient in plan.Ingredients ?? new())
                    craft.Children.AddRange(Map(ingredient, bazaar, depth + 1));
                craft.Children.AddRange(PurchaseSteps(plan.ItemId, purchases, 0, bazaar));
                craft.Children = Merge(craft.Children);
            }
            return [craft];
        }
        var fallbackKind = string.Equals(plan.Method, "npc", StringComparison.OrdinalIgnoreCase)
            ? StepKind.Npc
            : bazaar.Contains(plan.ItemId) ? StepKind.InstaBuy : StepKind.Auction;
        var steps = PurchaseSteps(plan.ItemId, purchases, plan.Quantity, bazaar);
        if (steps.Count == 0)
            steps.Add(new GoalStep { Kind = fallbackKind, ItemTag = plan.ItemId, Quantity = plan.Quantity });
        return Merge(steps);
    }

    // expected: when above the purchase sum the shortfall is added to the first bazaar step
    private static List<GoalStep> PurchaseSteps(string tag, List<AcquisitionFill> purchases, long expected, ISet<string> bazaar)
    {
        var steps = new List<GoalStep>();
        foreach (var fill in purchases)
        {
            if (fill.Quantity <= 0)
                continue;
            var source = fill.Source?.ToLowerInvariant();
            var kind = source switch
            {
                "order" => StepKind.BuyOrder,
                "npc" => StepKind.Npc,
                _ => bazaar.Contains(tag) ? StepKind.InstaBuy : StepKind.Auction
            };
            steps.Add(new GoalStep { Kind = kind, ItemTag = tag, Quantity = fill.Quantity, UnitPrice = fill.UnitPrice > 0 ? fill.UnitPrice : null });
        }
        steps = Merge(steps);
        var shortfall = expected - steps.Sum(s => s.Quantity);
        if (shortfall > 0 && steps.Count > 0)
        {
            var target = steps.FirstOrDefault(s => s.Kind is StepKind.BuyOrder or StepKind.InstaBuy) ?? steps[0];
            target.Quantity += shortfall;
        }
        return steps;
    }

    private static List<GoalStep> Merge(List<GoalStep> steps)
    {
        var result = new List<GoalStep>();
        foreach (var step in steps)
        {
            var existing = step.Children.Count == 0 && step.Kind != StepKind.Craft && step.Kind != StepKind.Forge && step.Kind != StepKind.Sell
                ? result.FirstOrDefault(r => r.Children.Count == 0 && r.Kind == step.Kind && r.ItemTag == step.ItemTag && r.Note == step.Note)
                : null;
            if (existing != null)
            {
                existing.UnitPrice = WeightedPrice([(existing.Quantity, existing.UnitPrice), (step.Quantity, step.UnitPrice)]);
                existing.Quantity += step.Quantity;
            }
            else
                result.Add(step);
        }
        return result;
    }

    /// <summary>Quantity weighted average of the known unit prices, null when none is known.</summary>
    public static double? WeightedPrice(IEnumerable<(long quantity, double? price)> parts)
    {
        var known = parts.Where(p => p.price.HasValue && p.quantity > 0).ToList();
        var total = known.Sum(p => p.quantity);
        return total == 0 ? null : known.Sum(p => p.quantity * p.price.Value) / total;
    }
}
