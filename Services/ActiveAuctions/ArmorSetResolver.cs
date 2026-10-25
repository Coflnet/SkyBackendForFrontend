using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Knows the armor sets (museum sets) by name so searches like "final destination" can be expanded to all pieces
/// </summary>
public class ArmorSetResolver
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private sealed record SetInfo(string Key, string Name, string[] Tags, string[][] Phrases);
    private sealed record State(List<SetInfo> Sets, Dictionary<string, SetInfo> ByName, DateTime LoadedAt, bool Empty);

    private readonly Func<Dictionary<string, HashSet<string>>> setsSource;
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> namesSource;
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private volatile State state;

    /// <summary>
    /// Creates the resolver on top of the item services
    /// </summary>
    public ArmorSetResolver(HypixelItemService itemService, ItemFilterSearchService search)
        : this(itemService.GetArmorSets, search.GetTagNamesAsync)
    {
    }

    internal ArmorSetResolver(Func<Dictionary<string, HashSet<string>>> setsSource,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> namesSource)
    {
        this.setsSource = setsSource;
        this.namesSource = namesSource;
    }

    /// <summary>
    /// Makes sure the sets are loaded, call before <see cref="TryMatch"/> or <see cref="GetTags"/>
    /// </summary>
    public virtual async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (IsFresh(state))
            return;
        await loadLock.WaitAsync(ct);
        try
        {
            if (IsFresh(state))
                return;
            var sets = setsSource();
            if (sets == null || sets.Count == 0)
            {
                state = new State(new(), new(), DateTime.UtcNow - Lifetime + RetryAfterFailure, true);
                return;
            }
            IReadOnlyDictionary<string, string> names = null;
            try { names = await namesSource(ct); } catch (Exception e) when (e is not OperationCanceledException) { }
            names ??= new Dictionary<string, string>();
            state = Build(sets, names);
        }
        finally
        {
            loadLock.Release();
        }
    }

    private static bool IsFresh(State s) => s != null && DateTime.UtcNow - s.LoadedAt < Lifetime;

    private static State Build(Dictionary<string, HashSet<string>> sets, IReadOnlyDictionary<string, string> names)
    {
        var list = new List<SetInfo>();
        var byName = new Dictionary<string, SetInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, pieces) in sets)
        {
            var tags = pieces.Select(p => p.ToUpperInvariant()).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray();
            var keyWords = key.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(ItemFilterSearchService.NormalizeWord).Where(w => w.Length > 0).ToArray();
            var common = CommonPrefix(tags.Select(t => names.TryGetValue(t, out var n)
                ? ItemFilterSearchService.NormalizePhrase(n).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : null).ToList());
            var display = common.Length > 0
                ? string.Join(' ', common.Select(Title))
                : string.Join(' ', keyWords.Select(Title));
            var phrases = new List<string[]>();
            if (common.Length > 0)
                phrases.Add(common);
            if (keyWords.Length > 0)
                phrases.Add(keyWords);
            var info = new SetInfo(key.ToUpperInvariant(), display, tags, phrases.ToArray());
            list.Add(info);
            byName.TryAdd(info.Key, info);
            byName.TryAdd(display, info);
        }
        return new State(list, byName, DateTime.UtcNow, false);
    }

    private static string Title(string w) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w);

    private static string[] CommonPrefix(List<string[]> all)
    {
        if (all.Count == 0 || all.Any(a => a == null || a.Length == 0))
            return Array.Empty<string>();
        var first = all[0];
        var n = 0;
        while (n < first.Length && all.All(a => a.Length > n && a[n] == first[n]))
            n++;
        // a piece name that is only the prefix (eg "Necron") means the prefix is not a set name
        return all.Any(a => a.Length == n) ? Array.Empty<string>() : first.Take(n).ToArray();
    }

    /// <summary>
    /// Tries to match an armor set name at <paramref name="startIndex"/>, the longest match wins
    /// </summary>
    /// <param name="words">normalized lowercase words</param>
    public virtual bool TryMatch(string[] words, int startIndex, out string setKey, out string[] tags, out int consumed)
    {
        setKey = null;
        tags = null;
        consumed = 0;
        var s = state;
        if (s == null || s.Empty)
            return false;
        SetInfo best = null;
        foreach (var set in s.Sets)
            foreach (var phrase in set.Phrases)
            {
                if (phrase.Length <= consumed || startIndex + phrase.Length > words.Length)
                    continue;
                var ok = true;
                for (var i = 0; i < phrase.Length && ok; i++)
                    ok = words[startIndex + i] == phrase[i];
                if (!ok)
                    continue;
                best = set;
                consumed = phrase.Length;
            }
        if (best == null)
            return false;
        setKey = best.Key;
        tags = best.Tags;
        return true;
    }

    /// <summary>
    /// Piece tags of a set by key or display name, null if unknown (or not loaded yet)
    /// </summary>
    public virtual string[] GetTags(string setKeyOrName)
    {
        if (string.IsNullOrWhiteSpace(setKeyOrName) || state == null)
            return null;
        var wanted = setKeyOrName.Trim();
        if (state.ByName.TryGetValue(wanted, out var set)
            || state.ByName.TryGetValue(wanted.Replace(' ', '_').ToUpperInvariant(), out set))
            return set.Tags;
        return null;
    }

    /// <summary>Display name of a set, eg "Final Destination"</summary>
    public virtual string GetName(string setKey)
        => state?.ByName.TryGetValue(setKey ?? "", out var set) == true ? set.Name : setKey;
}
