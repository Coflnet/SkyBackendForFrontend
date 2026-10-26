using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Turns free text like "final destination 7 sharpness" into a name-contains search plus filters for the active auction explorer.
/// Armor sets and single items are only offered as <see cref="ResolvedActiveQuery.Suggestions"/>, except for explicit set words ("ender armor")
/// </summary>
public class ActiveSearchQueryResolver
{
    private const int MaxWords = 12;
    private const int MaxSuggestions = 5;

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
        var nameWords = await itemSearch.GetNameWordsAsync(ct);
        var parsed = parser.Parse(words, new FilterParseOptions(nameWords == null ? null : nameWords.Contains));
        var unmatched = parsed.Unmatched;
        var phrase = unmatched.Length == 0 ? null : string.Join(' ', unmatched);
        var lookupWords = unmatched.Select(ItemFilterSearchService.NormalizeWord).Where(w => w.Length > 0).ToArray();
        var lookupPhrase = string.Join(' ', lookupWords);

        // explicit set intent ("ender armor"): "Ender Helmet" does not contain "ender armor" so a contains search would find nothing
        if (lookupWords.Any(ArmorSetResolver.GenericWords.Contains)
            && armorSets.TryMatchPhrase(lookupWords, out var setKey, out var setTags))
        {
            var setLabel = armorSets.GetName(setKey) + " set";
            return new ResolvedActiveQuery(setTags, setKey, parsed.Filters, null,
                string.Join(", ", new[] { setLabel, parsed.Description }.Where(x => !string.IsNullOrEmpty(x))));
        }

        var suggestions = new List<ActiveQuerySuggestion>();
        if (phrase != null)
        {
            foreach (var c in armorSets.FindCandidates(lookupWords))
                suggestions.Add(new ActiveQuerySuggestion("armorSet", c.Key, c.Name + " set", c.Tags));
            if (lookupPhrase.Length > 0)
            {
                var exact = await itemSearch.TryExactItemAsync(lookupPhrase, ct);
                if (exact != null)
                    suggestions.Add(new ActiveQuerySuggestion("item", exact.Tag, exact.Name, new[] { exact.Tag }));
                var single = (await itemSearch.FindNameMatchesAsync(lookupPhrase, ct))?.Single;
                if (single != null && single.Tag != exact?.Tag)
                    suggestions.Add(new ActiveQuerySuggestion("item", single.Tag, single.Name, new[] { single.Tag }));
            }
        }
        var description = string.Join(", ", new[] { phrase == null ? null : $"Name contains '{phrase}'", parsed.Description }
            .Where(x => !string.IsNullOrEmpty(x)));
        return new ResolvedActiveQuery(Array.Empty<string>(), null, parsed.Filters, phrase, description,
            suggestions.Take(MaxSuggestions).ToArray());
    }
}
