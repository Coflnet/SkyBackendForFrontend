
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Matches multiple item used by or related to farming")]
public class IsFarmingItemDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => ["Any", "Tool", "Armor", "None"];
    public FilterType FilterType => FilterType.Equal;

    private HashSet<string> Tools = ["THEORETICAL_HOE_WHEAT_1",
    "THEORETICAL_HOE_WHEAT_2",
    "THEORETICAL_HOE_WHEAT_3",
    "THEORETICAL_HOE_CARROT_1",
    "THEORETICAL_HOE_CARROT_2",
    "THEORETICAL_HOE_CARROT_3",
    "THEORETICAL_HOE_POTATO_1",
    "THEORETICAL_HOE_POTATO_2",
    "THEORETICAL_HOE_POTATO_3",
    "THEORETICAL_HOE_CANE_1",
    "THEORETICAL_HOE_CANE_2",
    "THEORETICAL_HOE_CANE_3",
    "THEORETICAL_HOE_WARTS_1",
    "THEORETICAL_HOE_WARTS_2",
    "THEORETICAL_HOE_WARTS_3",
    "THEORETICAL_HOE",
    "BASKET_OF_SEEDS",
    "NETHER_WART_POUCH",
    "COCO_CHOPPER",
    "CACTUS_KNIFE",
    "FUNGI_CUTTER",
    "PUMPKIN_DICER",
    "PUMPKIN_DICER_2",
    "PUMPKIN_DICER_3",
    "MELON_DICER",
    "MELON_DICER_2",
    "MELON_DICER_3",
    "HOE_OF_GREAT_TILLING",
    "HOE_OF_GREATEST_TILLING"];

    private HashSet<string> Armor = ["FARM_SUIT_HELMET",
    "FARM_SUIT_CHESTPLATE",
    "FARM_SUIT_LEGGINGS",
    "FARM_SUIT_BOOTS",
    "FARM_ARMOR_HELMET",
    "FARM_ARMOR_CHESTPLATE",
    "FARM_ARMOR_LEGGINGS",
    "FARM_ARMOR_BOOTS",
    "PUMPKIN_HELMET",
    "PUMPKIN_CHESTPLATE",
    "PUMPKIN_LEGGINGS",
    "PUMPKIN_BOOTS",
    "ENCHANTED_JACK_O_LANTERN",
    "FARMER_BOOTS",
    "RANCHERS_BOOTS",
    "CACTUS_HELMET",
    "CACTUS_CHESTPLATE",
    "CACTUS_LEGGINGS",
    "CACTUS_BOOTS",
    "MUSHROOM_HELMET",
    "MUSHROOM_CHESTPLATE",
    "MUSHROOM_LEGGINGS",
    "MUSHROOM_BOOTS",
    "RABBIT_HELMET",
    "RABBIT_CHESTPLATE",
    "RABBIT_LEGGINGS",
    "RABBIT_BOOTS",
    "MELON_HELMET",
    "MELON_CHESTPLATE",
    "MELON_LEGGINGS",
    "MELON_BOOTS",
    "CROPIE_HELMET",
    "CROPIE_CHESTPLATE",
    "CROPIE_LEGGINGS",
    "CROPIE_BOOTS",
    "SQUASH_HELMET",
    "SQUASH_CHESTPLATE",
    "SQUASH_LEGGINGS",
    "SQUASH_BOOTS",
    "FERMENTO_HELMET",
    "FERMENTO_CHESTPLATE",
    "FERMENTO_LEGGINGS",
    "FERMENTO_BOOTS"];

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        if (val == "Any")
        {
            return (f) => Tools.Contains(f.Auction.Tag) || Armor.Contains(f.Auction.Tag);
        }
        else if (val == "Tool")
        {
            return (f) => Tools.Contains(f.Auction.Tag);
        }
        else if (val == "Armor")
        {
            return (f) => Armor.Contains(f.Auction.Tag);
        }
        else if (val == "None")
        {
            return (f) => !Tools.Contains(f.Auction.Tag) && !Armor.Contains(f.Auction.Tag);
        }
        throw new CoflnetException("unknown_option", "Unknown option for IsFarmingItem");
    }
}