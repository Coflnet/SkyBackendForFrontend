using System;
using System.Collections.Generic;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>Sort order of the active auction explorer</summary>
public enum ActiveSortOrder
{
    LOWEST_PRICE,
    HIGHEST_PRICE,
    ENDING_SOON,
    NEWEST
}

/// <summary>
/// Query against the in-memory set of active auctions
/// </summary>
/// <param name="Filters">filter engine filters (already cleaned of paging keys)</param>
/// <param name="Tags">item tags to restrict to, empty for all items</param>
/// <param name="ArmorSet">armor set key or name, resolved to its piece tags</param>
/// <param name="Categories">item categories, resolved to their tags</param>
/// <param name="ItemNameContains">case insensitive part of the item name</param>
/// <param name="OrderBy">sort order</param>
/// <param name="Page">zero based page</param>
/// <param name="PageSize">1-100</param>
/// <param name="PerTagLimit">keep at most this many results per item tag</param>
/// <param name="SellerId">restrict to auctions of this seller (uuid without dashes)</param>
public record ActiveAuctionQuery(
    Dictionary<string, string> Filters,
    string[] Tags,
    string ArmorSet,
    string[] Categories,
    string ItemNameContains,
    ActiveSortOrder OrderBy,
    int Page,
    int PageSize,
    int? PerTagLimit,
    string SellerId = null);

/// <summary>
/// Compact auction representation returned by the explorer
/// </summary>
public class ActiveAuctionPreview
{
    public string Uuid { get; set; }
    public string Tag { get; set; }
    public string ItemName { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public Tier Tier { get; set; }
    public bool Bin { get; set; }
    public long StartingBid { get; set; }
    public long HighestBidAmount { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int Count { get; set; }
    public string AuctioneerId { get; set; }
    /// <summary>Display name of the seller, null if unknown</summary>
    [JsonProperty("playerName")]
    public string PlayerName { get; set; }
    /// <summary>The attributes that matched the filters, eg sharpness=7 or color=FF0000</summary>
    public Dictionary<string, string> Highlights { get; set; } = new();
}

/// <summary>Result page of an explorer query</summary>
/// <param name="Items">the previews of this page</param>
/// <param name="TotalCount">number of matches over all pages</param>
/// <param name="TagCounts">matches per item tag (null without store)</param>
/// <param name="FromStore">true if answered from memory, false for the database fallback</param>
/// <param name="SnapshotAt">when the data was snapshotted</param>
/// <param name="ResolvedTags">effective tag set after expansion, empty when all items are searched</param>
/// <param name="Suggestions">selectable narrowings of the free text query (armor set / item), empty without q</param>
public record ActiveAuctionQueryResult(
    List<ActiveAuctionPreview> Items,
    int TotalCount,
    Dictionary<string, int> TagCounts,
    bool FromStore,
    DateTime SnapshotAt,
    string[] ResolvedTags = null,
    ActiveQuerySuggestion[] Suggestions = null)
{
    /// <summary>Narrowing offers for a free text query, never null (empty without <c>q</c>)</summary>
    public ActiveQuerySuggestion[] Suggestions { get; init; } = Suggestions ?? Array.Empty<ActiveQuerySuggestion>();
}

/// <summary>Free text search interpreted as tags and filters</summary>
public record ResolvedActiveQuery(
    string[] Tags,
    string ArmorSet,
    Dictionary<string, string> Filters,
    string ItemNameContains,
    string Description,
    ActiveQuerySuggestion[] Suggestions = null)
{
    /// <summary>Optional narrowing offers, never null</summary>
    public ActiveQuerySuggestion[] Suggestions { get; init; } = Suggestions ?? Array.Empty<ActiveQuerySuggestion>();
}

/// <summary>A selectable narrowing of a name search</summary>
/// <param name="Kind">"armorSet" or "item"</param>
/// <param name="Key">set key or item tag</param>
/// <param name="Label">display text, eg "Ender set"</param>
/// <param name="Tags">tags to search when selected (set pieces or the single item)</param>
public record ActiveQuerySuggestion(string Kind, string Key, string Label, string[] Tags);
