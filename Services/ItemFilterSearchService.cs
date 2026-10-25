using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Shared mapping from SkyItems data to the item search result format
/// </summary>
public static class ItemSearchResults
{
    /// <summary>
    /// Creates the plain item result (name suffix by flags, hit count by auction availability)
    /// </summary>
    public static SearchResultItem ToResult(string text, string tag, ItemFlags? flags, Core.Tier tier)
    {
        var f = flags ?? ItemFlags.NONE;
        return new SearchResultItem(new ItemDetails.ItemSearchResult()
        {
            Name = text + (f.HasFlag(ItemFlags.BAZAAR) ? " - bazaar"
                    : f.HasFlag(ItemFlags.AUCTION) ? "" : " - not on ah"),
            Tag = tag,
            IconUrl = "https://sky.coflnet.com/static/icon/" + tag,
            HitCount = f.HasFlag(ItemFlags.AUCTION) ? 50 : 0,
            Tier = tier
        });
    }

    /// <summary>
    /// Tier of a SkyItems search result, same numbering as <see cref="Core.Tier"/>
    /// (the item search used to subtract one, which reported Hyperion as EPIC)
    /// </summary>
    public static Core.Tier FromSearchTier(Items.Client.Model.Tier? tier)
        => tier == null ? Core.Tier.UNKNOWN : (Core.Tier)tier;

    /// <summary>
    /// Tier of a full SkyItems item, same numbering as <see cref="Core.Tier"/>
    /// </summary>
    public static Core.Tier FromItemTier(Items.Client.Model.Tier? tier)
        => tier == null ? Core.Tier.UNKNOWN : (Core.Tier)tier;
}

/// <summary>
/// Outcome of <see cref="ItemFilterSearchService.TryParseAsync"/>
/// </summary>
/// <param name="Results">filter result, item result, or empty</param>
/// <param name="TimedOut">true if the time budget was hit, the answer is incomplete and must not be cached</param>
public record FilterSearchResult(List<SearchResultItem> Results, bool TimedOut = false);

/// <summary>An item found in free text</summary>
/// <param name="Tag">item tag</param>
/// <param name="Name">display name</param>
/// <param name="Consumed">how many words were used</param>
public record ResolvedItem(string Tag, string Name, int Consumed);

/// <summary>How many distinct items match a phrase by name</summary>
/// <param name="Prefix">items whose name starts with the phrase</param>
/// <param name="Contains">items whose name contains the phrase anywhere (includes the prefix matches)</param>
/// <param name="Single">the item if exactly one name starts with the phrase, else null</param>
public record ItemNameMatches(int Prefix, int Contains, ResolvedItem Single);


/// <summary>
/// Resolves search phrases like "hyperion sharp 7" to an item plus filters
/// </summary>
public class ItemFilterSearchService : IDisposable
{
    /// <summary>Maximum number of cached lookups</summary>
    public const int CacheEntryLimit = 2000;
    /// <summary>Longer prefixes are user input noise, they are neither looked up nor cached</summary>
    internal const int MaxPrefixLength = 60;
    private const int MaxWords = 8;
    private static readonly TimeSpan DetailsBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NamesRefresh = TimeSpan.FromHours(1);

    private readonly IItemsApi itemsApi;
    private readonly FilterQueryParser parser;
    // Private cache bounded by SizeLimit: every entry has Size = 1, so at most CacheEntryLimit (2000) small entries
    // (keys are <= ~70 chars, values a SearchResult or flags+tier) stay, roughly 1 MB worst case.
    // The name list is separate and bounded by the number of items.
    private readonly MemoryCache cache;
    private sealed record SearchBox(SearchResult Hit);
    private sealed record DetailsInfo(ItemFlags? Flags, Items.Client.Model.Tier? Tier);
    private sealed record DetailsBox(DetailsInfo Info);

    /// <summary>Number of cached lookups, for tests</summary>
    internal int CachedEntries => cache.Count;

    /// <summary>
    /// How long a prefix lookup stays cached, misses expire quickly so new items show up
    /// </summary>
    internal static TimeSpan LookupLifetime(bool found) => found ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(5);
    private readonly ILogger<ItemFilterSearchService> logger;

    private sealed record NameEntry(string Tag, string Name, string Lower);
    private sealed record NameList(List<NameEntry> Entries, Dictionary<string, NameEntry> Exact, DateTime LoadedAt);
    private sealed record Resolved(string Tag, string Name, ItemFlags? Flags, Core.Tier Tier, int Consumed);

    private NameList names;
    private Task<NameList> loading;
    private readonly object loadLock = new();

    /// <summary>
    /// Creates a new instance of <see cref="ItemFilterSearchService"/>
    /// </summary>
    public ItemFilterSearchService(IItemsApi itemsApi, FilterQueryParser parser, ILogger<ItemFilterSearchService> logger, int cacheEntryLimit = CacheEntryLimit)
    {
        this.itemsApi = itemsApi;
        this.parser = parser;
        this.cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheEntryLimit });
        this.logger = logger;
        if (itemsApi != null)
            loading = Task.Run(LoadNames); // warm the name list so the first user request is fast
    }

    /// <summary>
    /// Returns [filter result, item result], [item result] or an empty list
    /// </summary>
    public virtual async Task<FilterSearchResult> TryParseAsync(string query, CancellationToken ct)
    {
        var words = (query ?? "").ToLowerInvariant().Replace('’', '\'')
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => NormalizeWord(w).Length > 0).ToArray(); // pure punctuation carries no meaning
        var lookupWords = words.Select(NormalizeWord).ToArray();
        if (words.Length < 2 || words.Length > MaxWords)
            return new FilterSearchResult(new List<SearchResultItem>());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Budget);
        SearchResultItem plain = null;
        try
        {
            var item = await ResolveItem(lookupWords, timeout.Token);
            if (item == null)
                return new FilterSearchResult(new List<SearchResultItem>());
            plain = ItemSearchResults.ToResult(item.Name, item.Tag, item.Flags, item.Tier);
            var parsed = parser.Parse(words.Skip(item.Consumed));
            if (parsed.Filters.Count == 0)
                return new FilterSearchResult(new List<SearchResultItem> { plain });
            var filter = new SearchResultItem
            {
                Type = "filter",
                Id = FilterQueryParser.FormatId(item.Tag, parsed.Filters),
                Name = $"{item.Name} · {parsed.Description}",
                IconUrl = "https://sky.coflnet.com/static/icon/" + item.Tag,
                Tier = plain.Tier,
                HitCount = 50
            };
            return new FilterSearchResult(new List<SearchResultItem> { filter, plain });
        }
        catch (OperationCanceledException)
        {
            var timedOut = timeout.IsCancellationRequested && !ct.IsCancellationRequested;
            logger.LogDebug("Filter search for {query} was cancelled, timeout {timedOut}", query, timedOut);
            return new FilterSearchResult(plain == null ? new List<SearchResultItem>() : new List<SearchResultItem> { plain }, timedOut);
        }
    }

    /// <summary>
    /// Resolves an item from the start of the (normalized) words, the item may use all of them.
    /// </summary>
    public virtual async Task<ResolvedItem> ResolveItemAsync(string[] words, CancellationToken ct)
    {
        var r = await ResolveItem(words, ct, true);
        return r == null ? null : new ResolvedItem(r.Tag, r.Name, r.Consumed);
    }

    /// <summary>
    /// Item with exactly this normalized name, null if there is none
    /// </summary>
    public virtual async Task<ResolvedItem> TryExactItemAsync(string normalizedPhrase, CancellationToken ct)
    {
        var list = await GetNames(ct);
        var entry = list?.Exact.GetValueOrDefault(normalizedPhrase);
        return entry == null ? null : new ResolvedItem(entry.Tag, entry.Name, normalizedPhrase.Split(' ').Length);
    }

    /// <summary>
    /// Counts the items whose name starts with / contains the normalized phrase
    /// </summary>
    public virtual async Task<ItemNameMatches> FindNameMatchesAsync(string normalizedPhrase, CancellationToken ct)
    {
        var list = await GetNames(ct);
        if (list == null || string.IsNullOrEmpty(normalizedPhrase))
            return new ItemNameMatches(0, 0, null);
        var prefix = list.Entries.Where(e => e.Lower.StartsWith(normalizedPhrase)).GroupBy(e => e.Tag).Select(g => g.First()).ToList();
        var contains = list.Entries.Where(e => e.Lower.Contains(normalizedPhrase)).Select(e => e.Tag).Distinct().Count();
        var single = prefix.Count == 1 ? new ResolvedItem(prefix[0].Tag, prefix[0].Name, normalizedPhrase.Split(' ').Length) : null;
        return new ItemNameMatches(prefix.Count, contains, single);
    }

    /// <summary>
    /// Tag to display name of all known items (from the cached name list)
    /// </summary>
    internal virtual async Task<IReadOnlyDictionary<string, string>> GetTagNamesAsync(CancellationToken ct)
    {
        var list = await GetNames(ct);
        var result = new Dictionary<string, string>();
        if (list != null)
            foreach (var e in list.Entries)
                result.TryAdd(e.Tag, e.Name);
        return result;
    }

    private Task<Resolved> ResolveItem(string[] words, CancellationToken ct) => ResolveItem(words, ct, false);

    private async Task<Resolved> ResolveItem(string[] words, CancellationToken ct, bool allowAllWords)
    {
        var extra = allowAllWords ? 1 : 0;
        var list = await GetNames(ct);
        if (list != null)
            for (var k = words.Length - 1 + extra; k >= 1; k--)
            {
                var candidate = string.Join(' ', words.Take(k));
                var entry = list.Exact.GetValueOrDefault(candidate);
                if (entry == null)
                {
                    var matches = list.Entries.Where(e => e.Lower.StartsWith(candidate)).Take(2).ToList();
                    if (matches.Count == 1 || matches.Count == 2 && matches[0].Tag == matches[1].Tag)
                        entry = matches[0];
                }
                if (entry == null)
                    continue;
                var details = await GetItemWithinBudget(entry.Tag, ct);
                return new Resolved(entry.Tag, entry.Name, details?.Flags ?? ItemFlags.AUCTION, ItemSearchResults.FromItemTier(details?.Tier), k);
            }
        // aliases and abbreviations (aote) only exist in the SkyItems search
        var lookups = await Task.WhenAll(Enumerable.Range(1, words.Length - 1 + extra)
            .Select(k => SearchSafe(string.Join(' ', words.Take(k)), ct)));
        for (var k = lookups.Length; k >= 1; k--)
        {
            var hit = lookups[k - 1];
            if (hit?.Tag != null)
                return new Resolved(hit.Tag, hit.Text, hit.Flags, ItemSearchResults.FromSearchTier(hit.Tier), k);
        }
        return null;
    }

    private async Task<SearchResult> SearchSafe(string prefix, CancellationToken ct)
    {
        try
        {
            return await SearchCached(prefix, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // the generated client wraps a cancelled request in an ApiException; that is the time budget, not a failed lookup
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("item search cancelled by the time budget", e, ct);
            logger.LogWarning(e, "Item search for {prefix} failed", prefix);
            return null;
        }
    }

    private async Task<SearchResult> SearchCached(string prefix, CancellationToken ct)
    {
        if (prefix.Length > MaxPrefixLength)
            return null;
        var key = "ifs:search:" + prefix;
        if (cache.TryGetValue(key, out SearchBox cached))
            return cached.Hit;
        // aliases rank bazaar items (AOTE_STONE, also flagged AUCTION) next to the real one, filters apply to auctions
        var result = await itemsApi.ItemsSearchTermGetAsync(prefix, 5, 0, ct);
        var hit = result?.FirstOrDefault(r => r.Flags?.HasFlag(ItemFlags.AUCTION) == true && !r.Flags.Value.HasFlag(ItemFlags.BAZAAR))
            ?? result?.FirstOrDefault(r => r.Flags?.HasFlag(ItemFlags.AUCTION) == true)
            ?? result?.FirstOrDefault();
        cache.Set(key, new SearchBox(hit), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = LookupLifetime(hit != null) });
        return hit;
    }

    private async Task<DetailsInfo> GetItemWithinBudget(string tag, CancellationToken ct)
    {
        // tier and flags are nice to have, a slow lookup must not empty the answer
        using var own = CancellationTokenSource.CreateLinkedTokenSource(ct);
        own.CancelAfter(DetailsBudget);
        try
        {
            return await GetItem(tag, own.Token).WaitAsync(own.Token);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && (e is OperationCanceledException || own.IsCancellationRequested))
        {
            logger.LogDebug("Item details for {tag} not available in time", tag);
            return null;
        }
    }

    /// <summary>
    /// Lowercase, drops a possessive 's and punctuation so "Necron's Chestplate" matches "necron chestplate"
    /// </summary>
    internal static string NormalizeWord(string word)
    {
        var w = word.ToLowerInvariant().Replace('’', '\'');
        if (w.EndsWith("'s"))
            w = w.Substring(0, w.Length - 2);
        return new string(w.Where(c => char.IsLetterOrDigit(c)).ToArray());
    }

    internal static string NormalizePhrase(string phrase)
        => string.Join(' ', phrase.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(NormalizeWord).Where(w => w.Length > 0));

    private async Task<DetailsInfo> GetItem(string tag, CancellationToken ct)
    {
        var key = "ifs:item:" + tag;
        if (cache.TryGetValue(key, out DetailsBox cached))
            return cached.Info;
        DetailsInfo info;
        var lifetime = TimeSpan.FromHours(1);
        try
        {
            var item = await itemsApi.ItemItemTagGetAsync(tag, null, 0, ct);
            info = item == null ? null : new DetailsInfo(item.Flags, item.Tier);
            if (info == null)
                lifetime = LookupLifetime(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("item lookup cancelled by the time budget", e, ct);
            logger.LogWarning(e, "Could not load item {tag} for filter search", tag);
            info = null;
            lifetime = TimeSpan.FromSeconds(30);
        }
        cache.Set(key, new DetailsBox(info), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = lifetime });
        return info;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<NameList> GetNames(CancellationToken ct)
    {
        var current = names;
        Task<NameList> task;
        lock (loadLock)
        {
            if (current != null && DateTime.UtcNow - current.LoadedAt < NamesRefresh)
                return current;
            if (loading == null || loading.IsCompleted)
                loading = Task.Run(LoadNames);
            task = loading;
        }
        if (current != null)
            return current; // never block on a refresh if a list exists
        return await task.WaitAsync(ct);
    }

    private async Task<NameList> LoadNames()
    {
        try
        {
            var loaded = await itemsApi.ItemNamesGetAsync(0, CancellationToken.None);
            var entries = (loaded ?? new()).Where(i => i.Tag != null && i.Name != null)
                .Select(i => new NameEntry(i.Tag, i.Name, NormalizePhrase(i.Name))).ToList();
            if (entries.Count == 0)
                return null;
            var exact = new Dictionary<string, NameEntry>();
            foreach (var e in entries)
                exact.TryAdd(e.Lower, e);
            return names = new NameList(entries, exact, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load item names for filter search");
            return null;
        }
    }
}
