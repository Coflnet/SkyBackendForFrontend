using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Computes the "craft savings" signal for a crafting <see cref="Ingredient"/> - how much
/// money is saved by crafting the ingredient yourself instead of buying it outright.
/// </summary>
public static class CraftSavingsCalculator
{
    /// <summary>
    /// Result of <see cref="Calculate(Ingredient)"/>.
    /// </summary>
    /// <param name="CraftSavings">Absolute coins saved by subcrafting this ingredient instead of buying it, 0 if not applicable.</param>
    /// <param name="CraftSavingsPercent">Savings expressed as a percentage of the buy-order cost, 0 if not applicable.</param>
    /// <param name="IsSubcraft">True if the crafting engine chose to craft this ingredient instead of buying it.</param>
    public record struct CraftSavingsResult(double CraftSavings, double CraftSavingsPercent, bool IsSubcraft);

    /// <summary>
    /// Determines whether and how much is saved by subcrafting a given ingredient.
    /// </summary>
    /// <remarks>
    /// <see cref="Ingredient.BuyOrderCost"/> holds the genuine cost of buying the ingredient outright,
    /// while <see cref="Ingredient.CraftCost"/> holds the cost of crafting it yourself. When the engine
    /// decided to craft the ingredient (<see cref="Ingredient.Type"/> == "craft") and crafting is cheaper
    /// than buying, the difference is money the player saves by subcrafting rather than buying on the
    /// bazaar/auction house. If the ingredient was bought instead, or crafting isn't actually cheaper,
    /// there is nothing to save and the result is all zeros.
    /// </remarks>
    /// <param name="ingredient">The ingredient to evaluate.</param>
    /// <returns>A <see cref="CraftSavingsResult"/> describing the savings.</returns>
    public static CraftSavingsResult Calculate(Ingredient ingredient)
    {
        var isSubcraft = ingredient.Type == "craft";
        var craftSavings = (isSubcraft && ingredient.CraftCost > 0 && ingredient.BuyOrderCost > ingredient.CraftCost)
            ? ingredient.BuyOrderCost - ingredient.CraftCost
            : 0;
        var craftSavingsPercent = (craftSavings > 0 && ingredient.BuyOrderCost > 0)
            ? craftSavings / ingredient.BuyOrderCost * 100
            : 0;
        return new CraftSavingsResult(craftSavings, craftSavingsPercent, isSubcraft);
    }
}
