using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Pure query execution over an <see cref="ActiveAuctionSnapshot"/>
/// </summary>
public static class ActiveAuctionQueryEngine
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// Runs the query
    /// </summary>
    /// <param name="snapshot">immutable view of the active auctions</param>
    /// <param name="query">the query, filters/tags in it are not used, pass the resolved ones</param>
    /// <param name="tags">resolved item tags, empty for all</param>
    /// <param name="filters">cleaned filters, used for highlights</param>
    /// <param name="matcher">compiled filter, null matches everything</param>
    public static ActiveAuctionQueryResult Execute(ActiveAuctionSnapshot snapshot, ActiveAuctionQuery query,
        ICollection<string> tags, Dictionary<string, string> filters, Func<SaveAuction, bool> matcher, bool narrow = true)
    {
        var candidates = GetCandidates(snapshot, query, tags, narrow ? filters : null);
        var contains = string.IsNullOrWhiteSpace(query.ItemNameContains) ? null : query.ItemNameContains.Trim();
        var matched = new List<SaveAuction>();
        foreach (var a in candidates)
        {
            if (query.SellerId != null && a.AuctioneerId != query.SellerId)
                continue;
            if (contains != null && (a.ItemName == null || a.ItemName.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0))
                continue;
            if (matcher != null && !Matches(matcher, a))
                continue;
            matched.Add(a);
        }

        var tagCounts = new Dictionary<string, int>();
        foreach (var a in matched)
        {
            var tag = a.Tag ?? "";
            tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
        }

        matched.Sort(GetComparer(query.OrderBy));

        IEnumerable<SaveAuction> limited = matched;
        if (query.PerTagLimit is > 0)
        {
            var seen = new Dictionary<string, int>();
            var keep = new List<SaveAuction>();
            foreach (var a in matched)
            {
                var tag = a.Tag ?? "";
                var n = seen.GetValueOrDefault(tag);
                if (n >= query.PerTagLimit.Value)
                    continue;
                seen[tag] = n + 1;
                keep.Add(a);
            }
            limited = keep;
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(0, query.Page);
        var items = limited.Skip(page * pageSize).Take(pageSize)
            .Select(a => ToPreview(a, filters)).ToList();
        return new ActiveAuctionQueryResult(items, matched.Count, tagCounts, true, snapshot.BuiltAt,
            tags?.ToArray() ?? Array.Empty<string>());
    }

    private static bool Matches(Func<SaveAuction, bool> matcher, SaveAuction a)
    {
        try
        {
            return matcher(a);
        }
        catch
        {
            return false; // one broken auction must not fail the query
        }
    }

    private static IEnumerable<SaveAuction> GetCandidates(ActiveAuctionSnapshot snapshot, ActiveAuctionQuery query,
        ICollection<string> tags, Dictionary<string, string> filters)
    {
        var lists = filters == null ? new List<int[]>() : NarrowingLists(snapshot, filters);
        var hasTags = tags != null && tags.Count > 0;
        if (lists.Count > 0)
        {
            if (hasTags)
                lists.Add(Sorted(tags.Distinct().Where(snapshot.ByTag.ContainsKey).Select(t => snapshot.ByTag[t])));
            else if (query.SellerId != null)
                lists.Add(snapshot.BySeller.TryGetValue(query.SellerId, out var seller) ? seller : Array.Empty<int>());
            return Intersect(lists).Select(i => snapshot.All[i]);
        }
        if (hasTags)
        {
            return tags.Distinct().Where(snapshot.ByTag.ContainsKey)
                .SelectMany(t => snapshot.ByTag[t]).Select(i => snapshot.All[i]);
        }
        if (query.SellerId != null)
        {
            return snapshot.BySeller.TryGetValue(query.SellerId, out var indices)
                ? indices.Select(i => snapshot.All[i])
                : Enumerable.Empty<SaveAuction>();
        }
        return snapshot.All;
    }

    private static int[] Sorted(IEnumerable<int[]> parts)
    {
        var list = parts.ToList();
        if (list.Count == 1)
            return list[0];
        var merged = list.SelectMany(l => l).ToArray();
        Array.Sort(merged);
        return merged;
    }

    /// <summary>
    /// Derives the sorted posting lists whose intersection is a superset of everything the filters can match.
    /// Values that do not parse cleanly are skipped (no narrowing for that filter), the matcher stays authoritative.
    /// </summary>
    internal static List<int[]> NarrowingLists(ActiveAuctionSnapshot snapshot, Dictionary<string, string> filters)
    {
        var lists = new List<int[]>();
        if (filters == null || filters.Count == 0)
            return lists;
        string levelValue = null;
        foreach (var (key, value) in filters)
            if (key.Equals("EnchantLvl", StringComparison.OrdinalIgnoreCase))
                levelValue = value;
        foreach (var (key, rawValue) in filters)
        {
            var value = rawValue?.Trim();
            if (string.IsNullOrEmpty(value) || value.Contains(','))
            {
                // color lists always need a color, everything else is not narrowed
                if (!string.IsNullOrEmpty(value) && IsColorKey(key) && !IsAnyOrNone(value))
                    AddColor(snapshot, lists, value);
                continue;
            }
            if (key.Equals("Enchantment", StringComparison.OrdinalIgnoreCase))
            {
                // a given level has to be a plain positive number, otherwise the level filter might match auctions without the enchant
                if (levelValue != null && !IsPositiveInt(levelValue))
                    continue;
                AddEnchant(snapshot, lists, value);
            }
            else if (key.Equals("Rarity", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAnyOrNone(value) && !IsNumeric(value) && Enum.IsDefined(typeof(Tier), value) && Enum.TryParse<Tier>(value, out var tier))
                    lists.Add(snapshot.ByTier.TryGetValue(tier, out var l) ? l : Array.Empty<int>());
            }
            else if (key.Equals("Reforge", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAnyOrNone(value) && !IsNumeric(value) && Enum.TryParse<ItemReferences.Reforge>(value, true, out var reforge)
                    && Enum.IsDefined(typeof(ItemReferences.Reforge), reforge) && reforge != ItemReferences.Reforge.Any)
                    lists.Add(snapshot.ByReforge.TryGetValue(reforge, out var l) ? l : Array.Empty<int>());
            }
            else if (key.Equals("Bin", StringComparison.OrdinalIgnoreCase))
            {
                if (value == "true")
                    lists.Add(snapshot.BinOnly);
            }
            else if (IsColorKey(key))
            {
                if (!IsAnyOrNone(value))
                    AddColor(snapshot, lists, value);
            }
            else if (IsPositiveInt(value) && Enum.TryParse<Enchantment.EnchantmentType>(key, true, out var perEnchant)
                && Enum.IsDefined(typeof(Enchantment.EnchantmentType), perEnchant) && !IsNumeric(key))
            {
                AddEnchant(snapshot, lists, perEnchant);
            }
        }
        return lists;
    }

    /// <summary>
    /// Exact colours (or comma lists of them) use the per value index, tolerance and pattern forms only need some color.
    /// </summary>
    private static void AddColor(ActiveAuctionSnapshot snapshot, List<int[]> lists, string value)
    {
        long[] values = null;
        if (!value.Contains('-') && !value.Contains('_') && !value.StartsWith("pattern:", StringComparison.OrdinalIgnoreCase)
            && !value.Split(',', ' ').Any(IsAnyOrNone))
        {
            try
            {
                values = Coflnet.Sky.Filter.ColorFilter.ParseFilterValues(value);
            }
            catch (Exception)
            {
                values = null; // the filter itself reports invalid input
            }
        }
        if (values == null || values.Length == 0)
        {
            lists.Add(snapshot.WithColor);
            return;
        }
        var parts = values.Distinct().Select(v => snapshot.ByColorValue.TryGetValue(v, out var l) ? l : Array.Empty<int>()).ToList();
        lists.Add(parts.Count == 1 ? parts[0] : MergeSorted(parts));
    }

    private static int[] MergeSorted(List<int[]> parts)
    {
        var merged = parts.SelectMany(l => l).Distinct().ToArray();
        Array.Sort(merged);
        return merged;
    }

    private static bool IsColorKey(string key)
        => key.Equals("Color", StringComparison.OrdinalIgnoreCase) || key.Equals("HexColorList", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnyOrNone(string value)
        => value.Equals("any", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumeric(string value) => value.Length > 0 && (char.IsDigit(value[0]) || value[0] == '-' || value[0] == '+');

    private static bool IsPositiveInt(string value) => int.TryParse(value.Trim(), System.Globalization.NumberStyles.None, null, out var n) && n >= 1;

    private static void AddEnchant(ActiveAuctionSnapshot snapshot, List<int[]> lists, string name)
    {
        if (IsAnyOrNone(name) || IsNumeric(name) || !Enum.TryParse<Enchantment.EnchantmentType>(name, true, out var type)
            || !Enum.IsDefined(typeof(Enchantment.EnchantmentType), type))
            return;
        AddEnchant(snapshot, lists, type);
    }

    private static void AddEnchant(ActiveAuctionSnapshot snapshot, List<int[]> lists, Enchantment.EnchantmentType type)
    {
        if (type == Enchantment.EnchantmentType.unknown || type.ToString().Equals("None", StringComparison.OrdinalIgnoreCase))
            return;
        lists.Add(snapshot.ByEnchantType.TryGetValue((byte)type, out var l) ? l : Array.Empty<int>());
    }

    /// <summary>Merge intersection of sorted ascending lists, smallest first</summary>
    internal static int[] Intersect(List<int[]> lists)
    {
        lists.Sort((a, b) => a.Length.CompareTo(b.Length));
        var result = lists[0];
        for (var n = 1; n < lists.Count && result.Length > 0; n++)
        {
            var other = lists[n];
            var buffer = new int[result.Length];
            int i = 0, j = 0, k = 0;
            while (i < result.Length && j < other.Length)
            {
                if (result[i] < other[j]) i++;
                else if (result[i] > other[j]) j++;
                else { buffer[k++] = result[i]; i++; j++; }
            }
            result = k == buffer.Length ? buffer : buffer.AsSpan(0, k).ToArray();
        }
        return result;
    }

    /// <summary>Current price of an auction</summary>
    public static long Price(SaveAuction a) => a.HighestBidAmount == 0 ? a.StartingBid : a.HighestBidAmount;

    private static Comparison<SaveAuction> GetComparer(ActiveSortOrder order) => order switch
    {
        ActiveSortOrder.HIGHEST_PRICE => (a, b) => Tie(Price(b).CompareTo(Price(a)), a, b),
        ActiveSortOrder.ENDING_SOON => (a, b) => Tie(a.End.CompareTo(b.End), a, b),
        ActiveSortOrder.NEWEST => (a, b) => Tie(b.Start.CompareTo(a.Start), a, b),
        _ => (a, b) => Tie(Price(a).CompareTo(Price(b)), a, b),
    };

    private static int Tie(int result, SaveAuction a, SaveAuction b) => result != 0 ? result : a.UId.CompareTo(b.UId);

    private static ActiveAuctionPreview ToPreview(SaveAuction a, Dictionary<string, string> filters) => new()
    {
        Uuid = a.Uuid,
        Tag = a.Tag,
        ItemName = a.ItemName,
        Tier = a.Tier,
        Bin = a.Bin,
        StartingBid = a.StartingBid,
        HighestBidAmount = a.HighestBidAmount,
        Start = a.Start,
        End = a.End,
        Count = a.Count,
        AuctioneerId = a.AuctioneerId,
        Highlights = HighlightExtractor.Extract(a, filters)
    };
}

/// <summary>
/// Picks the attributes of an auction that the filters asked for
/// </summary>
public static class HighlightExtractor
{
    public const int MaxHighlights = 6;

    public static Dictionary<string, string> Extract(SaveAuction a, Dictionary<string, string> filters)
    {
        var result = new Dictionary<string, string>();
        if (filters == null || filters.Count == 0)
            return result;
        Dictionary<string, string> nbt = null;
        try { nbt = a.FlatenedNBT; } catch { }
        foreach (var (key, value) in filters)
        {
            if (result.Count >= MaxHighlights)
                break;
            if (key.Equals("Enchantment", StringComparison.OrdinalIgnoreCase))
            {
                AddEnchant(a, value, result);
            }
            else if (key.Equals("EnchantLvl", StringComparison.OrdinalIgnoreCase))
            {
                continue; // reported with the Enchantment filter
            }
            else if (key.Equals("Rarity", StringComparison.OrdinalIgnoreCase))
            {
                result["Rarity"] = a.Tier.ToString();
            }
            else if (key.Equals("Reforge", StringComparison.OrdinalIgnoreCase))
            {
                result["Reforge"] = a.Reforge.ToString();
            }
            else if (Enum.TryParse<Enchantment.EnchantmentType>(key, true, out _))
            {
                AddEnchant(a, key, result);
            }
            else if (nbt != null)
            {
                var found = nbt.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (found.Key != null)
                    result[key] = found.Value;
            }
        }
        return result;
    }

    private static void AddEnchant(SaveAuction a, string name, Dictionary<string, string> result)
    {
        if (!Enum.TryParse<Enchantment.EnchantmentType>(name, true, out var type))
            return;
        var ench = a.Enchantments?.FirstOrDefault(e => e.Type == type);
        if (ench != null)
            result[type.ToString()] = ench.Level.ToString();
    }
}
