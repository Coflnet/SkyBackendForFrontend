using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Turns free text like "final destination 7 sharpness" into tags and filters for the active auction explorer
/// </summary>
public class ActiveSearchQueryResolver
{
    private const int MaxWords = 12;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    private readonly ItemFilterSearchService itemSearch;
    private readonly ArmorSetResolver armorSets;
    private readonly FilterQueryParser parser;

    public ActiveSearchQueryResolver(ItemFilterSearchService itemSearch, ArmorSetResolver armorSets, FilterQueryParser parser)
    {
        this.itemSearch = itemSearch;
        this.armorSets = armorSets;
        this.parser = parser;
    }

    /// <summary>
    /// Resolves the text, never throws for unknown words (they become <see cref="ResolvedActiveQuery.ItemNameContains"/>)
    /// </summary>
    public virtual async Task<ResolvedActiveQuery> ResolveAsync(string text, CancellationToken ct = default)
    {
        var words = (text ?? "").ToLowerInvariant().Replace('’', '\'')
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => ItemFilterSearchService.NormalizeWord(w).Length > 0).Take(MaxWords).ToArray();
        var lookup = words.Select(ItemFilterSearchService.NormalizeWord).ToArray();
        if (words.Length == 0)
            return new ResolvedActiveQuery(Array.Empty<string>(), null, new(), null, "");

        await armorSets.EnsureLoadedAsync(ct);
        string[] tags = Array.Empty<string>();
        string armorSet = null;
        string label = null;
        string nameContainsPhrase = null;
        var consumed = 0;

        for (var k = words.Length; k >= 1 && consumed == 0; k--)
        {
            var phrase = string.Join(' ', lookup.Take(k));
            var exact = await itemSearch.TryExactItemAsync(phrase, ct);
            if (exact != null)
            {
                tags = new[] { exact.Tag };
                label = exact.Name;
                consumed = k;
            }
            else if (armorSets.TryMatchPhrase(lookup.Take(k).ToArray(), out var setKey, out var setTags))
            {
                tags = setTags;
                armorSet = setKey;
                label = armorSets.GetName(setKey) + " set";
                consumed = k;
            }
            else
            {
                var matches = await itemSearch.FindNameMatchesAsync(phrase, ct);
                if (matches?.Single != null)
                {
                    tags = new[] { matches.Single.Tag };
                    label = matches.Single.Name;
                    consumed = k;
                }
                else if (matches != null && matches.Contains > 0)
                {
                    // ambiguous: span every item with this text in its name
                    nameContainsPhrase = phrase;
                    label = $"Name contains '{phrase}'";
                    consumed = k;
                }
            }
        }
        if (consumed == 0 && !FirstWordIsFilter(words))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Budget);
            ResolvedItem item = null;
            try { item = await itemSearch.ResolveItemAsync(lookup, timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            if (item?.Tag != null)
            {
                tags = new[] { item.Tag };
                label = item.Name;
                consumed = Math.Clamp(item.Consumed, 1, words.Length);
            }
        }

        var parsed = parser.Parse(words.Skip(consumed));
        var unmatched = parsed.Unmatched.Length == 0 ? null : string.Join(' ', parsed.Unmatched);
        var nameContains = nameContainsPhrase == null ? unmatched : unmatched == null ? nameContainsPhrase : nameContainsPhrase + " " + unmatched;
        var description = string.Join(", ", new[] { label, parsed.Description, unmatched == null ? null : $"\"{unmatched}\"" }
            .Where(s => !string.IsNullOrEmpty(s)));
        return new ResolvedActiveQuery(tags, armorSet, parsed.Filters, nameContains, description);
    }

    private bool FirstWordIsFilter(string[] words) => parser.Parse(words.Take(1)).Filters.Count > 0;
}
