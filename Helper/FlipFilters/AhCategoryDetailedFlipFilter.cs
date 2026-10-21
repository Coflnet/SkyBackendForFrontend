using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Model;

namespace Coflnet.Sky.Commands.Shared;
[FilterDescription("Ah category the item is in")]
public class AhCategoryDetailedFlipFilter : DetailedFlipFilter
{
    /// <summary>
    /// SkyItems <see cref="ItemCategory"/> sets an auction stored with <see cref="Category.UNKNOWN"/>
    /// is checked against. The updater keeps UNKNOWN whenever Hypixel's category string is missing
    /// or unrecognised, so an UNKNOWN auction whose tag is in one of these sets counts as the mapped
    /// AH category. Equipment (NECKLACE, CLOAK, BELT, GLOVES, BRACELET) and every other ItemCategory
    /// are deliberately unmapped; CONSUMABLES, BLOCKS, MISC, cosmetic and dyes compare as before.
    /// </summary>
    public static readonly IReadOnlyDictionary<Category, ItemCategory[]> UnknownCategoryLookup = new Dictionary<Category, ItemCategory[]>
    {
        [Category.ARMOR] = [ItemCategory.HELMET, ItemCategory.CHESTPLATE, ItemCategory.LEGGINGS, ItemCategory.BOOTS],
        [Category.WEAPON] = [ItemCategory.SWORD, ItemCategory.LONGSWORD, ItemCategory.BOW, ItemCategory.WAND, ItemCategory.FISHING_WEAPON, ItemCategory.GAUNTLET],
        [Category.ACCESSORIES] = [ItemCategory.ACCESSORY],
    };

    public object[] Options => Enum.GetValues<Category>().Select(t => (object)t).ToArray();

    public FilterType FilterType => FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        if (!Enum.TryParse<Category>(val, true, out Category ahCategory))
            throw new CoflnetException("invalid_category", $"the specified category {val} does not exist");

        if (ahCategory == Category.UNKNOWN)
        {
            // only auctions that no mapped set claims stay UNKNOWN
            var allMapped = UnknownCategoryLookup.Values.SelectMany(c => c).ToArray();
            var sets = LoadItemCategories(allMapped);
            return flip => flip.Auction.Category == Category.UNKNOWN && !InAnyCategory(sets, allMapped, flip.Auction.Tag);
        }
        if (!UnknownCategoryLookup.TryGetValue(ahCategory, out var mapped))
            return flip => flip.Auction.Category == ahCategory;

        var categories = LoadItemCategories(mapped);
        return flip => flip.Auction.Category == ahCategory
            || (flip.Auction.Category == Category.UNKNOWN && InAnyCategory(categories, mapped, flip.Auction.Tag));
    }

    /// <summary>
    /// Same loading as <see cref="ItemCategoryDetailedFlipFilter"/>: a missing set is fetched once here,
    /// a failed load throws, and the returned dictionary is re-read per flip so refreshes are picked up.
    /// </summary>
    private static ConcurrentDictionary<ItemCategory, HashSet<string>> LoadItemCategories(ItemCategory[] itemCategories)
    {
        var service = DiHandler.GetService<FilterStateService>();
        foreach (var itemCategory in itemCategories)
        {
            if (service.State.itemCategories.ContainsKey(itemCategory))
                continue;
            Activity.Current.Log($"Got itemCategory {itemCategory}, updating cache");
            service.GetItemCategory(itemCategory).Wait();
        }
        return service.State.itemCategories;
    }

    private static bool InAnyCategory(ConcurrentDictionary<ItemCategory, HashSet<string>> sets, ItemCategory[] itemCategories, string tag)
    {
        foreach (var itemCategory in itemCategories)
        {
            if (sets.TryGetValue(itemCategory, out var tags) && tags.Contains(tag))
                return true;
        }
        return false;
    }
}
