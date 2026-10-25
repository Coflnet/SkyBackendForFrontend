using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Answers explorer queries from the in-memory replica of active auctions
/// </summary>
public class ActiveAuctionQueryService
{
    public const int MaxTags = 64;
    private const int MatcherCacheSize = 256;
    private static readonly TimeSpan CategoryLifetime = TimeSpan.FromHours(1);

    private static readonly Histogram Duration = Metrics.CreateHistogram("sky_api_active_query_duration_seconds",
        "Duration of active auction explorer queries");

    /// <summary>Parameters of the endpoint that are no filters</summary>
    public static readonly string[] ReservedKeys = { "page", "pageSize", "orderBy", "tags", "armorSet", "categories", "q", "perTagLimit", "itemNameContains" };

    private readonly ActiveAuctionReplica replica;
    private readonly FilterEngine filterEngine;
    private readonly FilterStateService filterState;
    private readonly ArmorSetResolver armorSets;
    private readonly ConcurrentDictionary<ItemCategory, (string[] Tags, DateTime At)> categoryCache = new();
    private readonly object matcherLock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Func<SaveAuction, bool> Matcher)>> matchers = new();
    private readonly LinkedList<(string Key, Func<SaveAuction, bool> Matcher)> matcherOrder = new();

    private readonly IActiveAuctionNameSource nameSource;
    private readonly ILogger<ActiveAuctionQueryService> logger;
    private readonly MemoryCache nameCache = new(new MemoryCacheOptions { SizeLimit = 50_000 });
    private static readonly TimeSpan NameLifetime = TimeSpan.FromMinutes(10);
    private const int MaxNameBatch = 100;

    public ActiveAuctionQueryService(ActiveAuctionReplica replica, FilterEngine filterEngine, FilterStateService filterState, ArmorSetResolver armorSets,
        IActiveAuctionNameSource nameSource = null, ILogger<ActiveAuctionQueryService> logger = null)
    {
        this.nameSource = nameSource;
        this.logger = logger;
        this.replica = replica;
        this.filterEngine = filterEngine;
        this.filterState = filterState;
        this.armorSets = armorSets;
    }

    /// <summary>
    /// Drops everything that is no filter (paging, sorting, private keys)
    /// </summary>
    public static Dictionary<string, string> CleanFilters(IDictionary<string, string> filters)
    {
        var clean = new Dictionary<string, string>();
        if (filters == null)
            return clean;
        foreach (var (key, value) in filters)
        {
            if (string.IsNullOrWhiteSpace(key) || key.StartsWith('_') || string.IsNullOrEmpty(value))
                continue;
            if (ReservedKeys.Any(r => r.Equals(key, StringComparison.OrdinalIgnoreCase)))
                continue;
            clean[key] = value;
        }
        return clean;
    }

    /// <summary>
    /// Resolves explicit tags, the armor set and the categories into one distinct upper case tag list
    /// </summary>
    public virtual async Task<string[]> ResolveTagsAsync(ActiveAuctionQuery q)
    {
        var tags = new List<string>();
        if (q.Tags != null)
            tags.AddRange(q.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(q.ArmorSet))
        {
            await armorSets.EnsureLoadedAsync();
            var set = armorSets.GetTags(q.ArmorSet) ?? throw new CoflnetException("unknown_armor_set", $"The armor set {q.ArmorSet} is not known");
            tags.AddRange(set);
        }
        var result = tags.Distinct().Take(MaxTags).ToList();
        if (q.Categories != null)
            foreach (var name in q.Categories.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (!Enum.TryParse<ItemCategory>(name.Trim(), true, out var category))
                    throw new CoflnetException("invalid_category", $"the specified category {name} does not exist");
                result.AddRange(await GetCategoryTags(category));
            }
        return result.Distinct().ToArray();
    }

    private async Task<string[]> GetCategoryTags(ItemCategory category)
    {
        if (categoryCache.TryGetValue(category, out var cached) && DateTime.UtcNow - cached.At < CategoryLifetime)
            return cached.Tags;
        await filterState.GetItemCategory(category);
        var tags = filterState.State.itemCategories.TryGetValue(category, out var set)
            ? set.Select(t => t.ToUpperInvariant()).ToArray()
            : Array.Empty<string>();
        categoryCache[category] = (tags, DateTime.UtcNow);
        return tags;
    }

    /// <summary>
    /// Runs the query against the replica. Throws <see cref="UnknownFilterException"/> for unknown filters.
    /// </summary>
    public virtual async Task<ActiveAuctionQueryResult> QueryAsync(ActiveAuctionQuery q, CancellationToken ct = default)
    {
        using var timer = Duration.NewTimer();
        var tags = await ResolveTagsAsync(q);
        var filters = CleanFilters(q.Filters);
        var matcher = GetMatcher(filters);
        var snapshot = replica.Set.GetFreshSnapshot();
        var result = await Task.Run(() => ActiveAuctionQueryEngine.Execute(snapshot, q, tags, filters, matcher), ct);
        await FillPlayerNamesAsync(result?.Items);
        return result;
    }

    /// <summary>
    /// Fills <see cref="ActiveAuctionPreview.PlayerName"/> with one batch lookup for the distinct sellers. Never throws.
    /// </summary>
    internal async Task FillPlayerNamesAsync(List<ActiveAuctionPreview> items)
    {
        if (nameSource == null || items == null || items.Count == 0)
            return;
        try
        {
            var ids = items.Select(i => i.AuctioneerId).Where(i => !string.IsNullOrEmpty(i)).Distinct().Take(MaxNameBatch).ToList();
            var names = new Dictionary<string, string>();
            var missing = new List<string>();
            foreach (var id in ids)
            {
                if (nameCache.TryGetValue(id, out string cached))
                    names[id] = cached;
                else
                    missing.Add(id);
            }
            if (missing.Count > 0)
            {
                var resolved = await nameSource.GetNamesAsync(missing);
                foreach (var id in missing)
                {
                    if (resolved == null || !resolved.TryGetValue(id, out var name) || string.IsNullOrEmpty(name))
                        continue;
                    name = name.Trim('"');
                    names[id] = name;
                    nameCache.Set(id, name, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = NameLifetime });
                }
            }
            foreach (var item in items)
                if (item.AuctioneerId != null && names.TryGetValue(item.AuctioneerId, out var n))
                    item.PlayerName = n;
        }
        catch (Exception e)
        {
            logger?.LogDebug(e, "Could not resolve seller names for explorer page");
        }
    }

    internal Func<SaveAuction, bool> GetMatcher(Dictionary<string, string> filters)
    {
        if (filters.Count == 0)
            return null;
        var key = string.Join('&', filters.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase).Select(f => $"{f.Key}={f.Value}"));
        lock (matcherLock)
        {
            if (matchers.TryGetValue(key, out var node))
            {
                matcherOrder.Remove(node);
                matcherOrder.AddFirst(node);
                return node.Value.Matcher;
            }
        }
        var matcher = filterEngine.GetMatcher(filters); // throws UnknownFilterException
        lock (matcherLock)
        {
            if (matchers.ContainsKey(key))
                return matcher;
            matchers[key] = matcherOrder.AddFirst((key, matcher));
            while (matchers.Count > MatcherCacheSize)
            {
                var last = matcherOrder.Last;
                matcherOrder.RemoveLast();
                matchers.Remove(last.Value.Key);
            }
        }
        return matcher;
    }

    internal int CachedMatchers { get { lock (matcherLock) return matchers.Count; } }
}
