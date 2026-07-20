using Coflnet.Sky.Crafts.Client.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class CraftSavingsCalculatorTests
{
    [Test]
    public void NonCraftIngredientYieldsZero()
    {
        var ingredient = new Ingredient(itemId: "ENCHANTED_IRON", count: 1, cost: 100, buyOrderCost: 100, craftCost: 50, type: null);

        var result = CraftSavingsCalculator.Calculate(ingredient);

        Assert.That(result.IsSubcraft, Is.False);
        Assert.That(result.CraftSavings, Is.EqualTo(0));
        Assert.That(result.CraftSavingsPercent, Is.EqualTo(0));
    }

    [Test]
    public void CraftedIngredientCheaperThanBuyingYieldsSavings()
    {
        var ingredient = new Ingredient(itemId: "ENCHANTED_IRON", count: 1, cost: 50, buyOrderCost: 100, craftCost: 50, type: "craft");

        var result = CraftSavingsCalculator.Calculate(ingredient);

        Assert.That(result.IsSubcraft, Is.True);
        Assert.That(result.CraftSavings, Is.EqualTo(50));
        Assert.That(result.CraftSavingsPercent, Is.EqualTo(50));
    }

    [Test]
    public void CraftedIngredientNotCheaperYieldsNoSavings()
    {
        var ingredient = new Ingredient(itemId: "ENCHANTED_IRON", count: 1, cost: 100, buyOrderCost: 90, craftCost: 100, type: "craft");

        var result = CraftSavingsCalculator.Calculate(ingredient);

        Assert.That(result.IsSubcraft, Is.True);
        Assert.That(result.CraftSavings, Is.EqualTo(0));
        Assert.That(result.CraftSavingsPercent, Is.EqualTo(0));
    }

    [Test]
    public void ZeroBuyOrderCostDoesNotDivideByZero()
    {
        var ingredient = new Ingredient(itemId: "ENCHANTED_IRON", count: 1, cost: 50, buyOrderCost: 0, craftCost: 50, type: "craft");

        var result = CraftSavingsCalculator.Calculate(ingredient);

        Assert.That(result.IsSubcraft, Is.True);
        Assert.That(result.CraftSavings, Is.EqualTo(0));
        Assert.That(result.CraftSavingsPercent, Is.EqualTo(0));
    }
}
