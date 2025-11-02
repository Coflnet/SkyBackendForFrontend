using System;
using System.Runtime.Serialization;
using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Coflnet.Sky.Api.Models.Mod;

public class FieldDescription : Attribute
{
    public string[] Text { get; set; }

    public FieldDescription(params string[] text)
    {
        Text = text;
    }
}

/// <summary>
/// List of available fields
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum DescriptionField
{
    /// <summary>
    /// Display nothing (global)
    /// </summary>
    [FieldDescription("Placeholder for emtpy line")]
    NONE,
    /// <summary>
    /// Display the lowest bin
    /// </summary>
    [FieldDescription("Best matching lowest bin")]
    LBIN,
    /// <summary>
    /// Display the key used to get the lowest bin
    /// </summary>
    [FieldDescription("List of modifiers used to get the lowest bin")]
    LBIN_KEY,
    /// <summary>
    /// Display the median price
    /// </summary>
    [FieldDescription("Median price of items with all modifiers in MEDIAN_KEY")]
    MEDIAN,
    /// <summary>
    /// Display the key used to get the median price
    /// </summary>
    [FieldDescription("List of modifiers used to get the median price")]
    MEDIAN_KEY,
    /// <summary>
    /// Display the volume
    /// </summary>
    [FieldDescription("Sales per day of items with all modifiers in MEDIAN_KEY")]
    VOLUME,
    /// <summary>
    /// Display the item tag
    /// </summary>
    [FieldDescription("The hypixel internal item id")]
    TAG,
    /// <summary>
    /// Display the craft cost
    /// </summary>
    [FieldDescription("Craft cost of clean item")]
    CRAFT_COST,
    /// <summary>
    /// Display the bazaar buy cost
    /// </summary>
    [FieldDescription("The price you can buy an item on bazaar", "Gets hidden if not on bazaar")]
    BazaarBuy,
    /// <summary>
    /// Display the bazaar sell profit
    /// </summary>
    [FieldDescription("The price you can sell an item on bazaar", "Gets hidden if not sellable on bazaar")]
    BazaarSell,
    /// <summary>
    /// Display price paid
    /// </summary>
    [FieldDescription("The last price this item sold for", "any sell counts not just your own")]
    PRICE_PAID,
    /// <summary>
    /// Breakdown of relevant price modifying stats
    /// </summary>
    [FieldDescription("List of valuable attributes used to estimate value")]
    ITEM_KEY,
    /// <summary>
    /// Enchant Cost Summary
    /// </summary>
    [FieldDescription("Sum of all Enchantment Cost")]
    EnchantCost,
    /// <summary>
    /// Sum of gemstone value
    /// </summary>
    [FieldDescription("Sum of gemstone value")]
    GemValue,
    /// <summary>
    /// Summary of past list attempts
    /// </summary>
    [FieldDescription("Summary of past list attempts")]
    SpentOnAhFees,
    /// <summary>
    /// how much kat takes in coins and materials to upgrade
    /// </summary>
    [FieldDescription("how much kat takes in coins and materials to upgrade")]
    KatUpgradeCost,
    /// <summary>
    /// Estimated price the item instasells for
    /// </summary>
    [FieldDescription("Estimated price the item instasells for")]
    InstaSellPrice,
    [FieldDescription("Summary of the cost of all modifiers applied", "May be screwed by manipulated bazaar prices")]
    ModifierCost,
    [FieldDescription("Full craft cost including modifiers")]
    FullCraftCost,
    [FieldDescription("Modifiers included in cost", "lets you see what was summed up in the cost")]
    ModifierCostList,
    [FieldDescription(
        "List of flip finders, which",
        "deemed the last purchase a flip.",
        "Includes their estimated value",
        "§cThis can noticably slow description",
        "§cloading because of how its stored")]
    FinderEstimates,
    [FieldDescription(
        "How much the median estimate fluctuates",
        "Uses different time interval-medians",
        "to calculate the volatility")]
    Volatility,
    [FieldDescription(
        "The price the last reference with",
        "same valuable attributes sold for.",
        "§cNot necessarily the same item",
        "Watch out if this is very low")]
    LastSoldFor,
    [FieldDescription(
        "How long on average it takes to",
        "sell an item with the same ",
        "valuable attributes")]
    TimeToSell,
    [FieldDescription(
        "How much the item/stack will",
        "sell for in an npc shop")]
    NpcSellPrice,
    [FieldDescription(
        "Color codes with their source",
        "Highlights exotics like iTEM")]
    ColorCode,
    [FieldDescription(
        "Placeholder for the skyblock",
        "item description. Useful for",
        "when you want some data above",
        "the description directly below",
        "the item name")]
    DefaultLore,
    [FieldDescription(
        "Complex items may be better valued",
        "by our self learning price model",
        "Enable its estimate with this")]
    AiEstimate,
    // anything over 9000 gets hidden
    BAZAAR_COST = 9001
}
