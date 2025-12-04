using System.Linq;
using System.Collections.Generic;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Helper class for comparing items to detect changes since purchase
/// </summary>
public static class ItemComparisonHelper
{
    /// <summary>
    /// Creates a comparison key from an auction's FlatenedNBT and enchantment count.
    /// Used to detect if an item has been modified since purchase.
    /// </summary>
    /// <param name="auction">The auction to create a comparison key for</param>
    /// <returns>A string key representing the item state</returns>
    public static string GetComparisonKey(SaveAuction auction)
    {
        if (auction == null)
            return string.Empty;
        
        var nbtPart = string.Join(";", auction.FlatenedNBT?.OrderBy(f => f.Key).Select(kv => $"{kv.Key}:{kv.Value}") ?? []);
        var enchantCount = auction.Enchantments?.Count ?? 0;
        return $"{nbtPart}{enchantCount}";
    }

    /// <summary>
    /// Creates a comparison key from FlatenedNBT dictionary and enchantment count.
    /// Used to detect if an item has been modified since purchase.
    /// </summary>
    /// <param name="flatenedNbt">The FlatenedNBT dictionary</param>
    /// <param name="enchantmentCount">The number of enchantments on the item</param>
    /// <returns>A string key representing the item state</returns>
    public static string GetComparisonKey(Dictionary<string, string> flatenedNbt, int enchantmentCount)
    {
        var nbtPart = string.Join(";", flatenedNbt?.OrderBy(f => f.Key).Select(kv => $"{kv.Key}:{kv.Value}") ?? []);
        return $"{nbtPart}{enchantmentCount}";
    }

    /// <summary>
    /// Checks if an item has been changed by comparing the purchase auction state 
    /// with the current item state.
    /// </summary>
    /// <param name="purchaseAuction">The original auction from which the item was purchased</param>
    /// <param name="currentItem">The current state of the item</param>
    /// <returns>True if the item was changed, false otherwise</returns>
    public static bool WasItemChanged(SaveAuction purchaseAuction, SaveAuction currentItem)
    {
        if (purchaseAuction == null || currentItem == null)
            return false;
        
        return GetComparisonKey(purchaseAuction) != GetComparisonKey(currentItem);
    }
}
