using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Result of <see cref="FilterQueryParser.Parse(IEnumerable{string})"/>
/// </summary>
/// <param name="Filters">filter name to value, in the order the words were typed</param>
/// <param name="Unmatched">words that did not match any filter</param>
/// <param name="Description">human readable form eg "Sharpness 7, Heroic"</param>
public record ParsedFilters(Dictionary<string, string> Filters, string[] Unmatched, string Description);

/// <summary>
/// Switches <see cref="FilterQueryParser"/> into "search mode" where a bare word is only a filter if the intent is unambiguous.
/// Without options (default mode) the parser is as lenient as possible, which suits flip filter commands.
/// </summary>
/// <param name="IsNameWord">
/// true if the (lowercase) word is part of an item name. Such a word is never turned into a filter
/// unless an explicit value follows it ("sharpness 7"), rarity words are exempt. May be null (nothing is a name word).
/// </param>
/// <param name="RequireExplicitValues">
/// Words that resolve without an explicit value (implicit <c>&gt;0</c>/<c>true</c>/default) must match the filter name exactly,
/// no prefix/initials matching of enchants, filter names or reforge/rarity options.
/// </param>
public record FilterParseOptions(Func<string, bool> IsNameWord = null, bool RequireExplicitValues = true);

/// <summary>
/// Turns free text like "sharp 7 heroic recomb" into item filters.
/// Pure and in-memory, matching runs against the filters registered in the <see cref="FilterEngine"/>.
/// </summary>
public class FilterQueryParser
{
    private readonly FilterEngine engine;
    private readonly ILogger<FilterQueryParser> logger;
    private readonly object indexLock = new();
    private Index index;

    /// <summary>
    /// Explicit shorthands. Key is the lowercase (space joined) phrase, extend freely.
    /// <c>Value</c> is a fixed value, <c>Default</c> is used if no value was typed next to the word.
    /// Enchantment targets without a default get "&gt;0".
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Alias> Aliases = new Dictionary<string, Alias>
    {
        ["recomb"] = new("Recombobulated", "true"),
        ["recombed"] = new("Recombobulated", "true"),
        ["recombobulated"] = new("Recombobulated", "true"),
        ["hpb"] = new("HotPotatoCount", null, "10"),
        ["fpb"] = new("HotPotatoCount", "15"),
        ["clean"] = new("Clean", "yes"),
        ["shiny"] = new("IsShiny", "true"),
        ["bin"] = new("Bin", "true"),
        ["star"] = new("Stars", null, ">0"),
        ["stars"] = new("Stars", null, ">0"),
        ["starred"] = new("Stars", null, ">0"),
        ["lvl"] = new("PetLevel"),
        ["lv"] = new("PetLevel"),
        ["level"] = new("PetLevel"),
        ["perfect"] = new("PerfectGemsCount", null, ">0"),
        ["gems"] = new("PerfectGemsCount", null, ">0"),
        ["chim"] = new("ultimate_chimera"),
        ["wise"] = new("ultimate_wise"),
        ["legion"] = new("ultimate_legion"),
        ["ofa"] = new("ultimate_one_for_all"),
        ["se"] = new("ultimate_soul_eater"),
        ["soul eater"] = new("ultimate_soul_eater"),
        ["gk"] = new("giant_killer"),
        ["fs"] = new("first_strike"),
        ["ts"] = new("triple_strike"),
        ["triple"] = new("triple_strike"),
        ["es"] = new("ender_slayer"),
        ["ender"] = new("ender_slayer"),
        ["tl"] = new("thunderlord"),
        ["dh"] = new("dragon_hunter"),
        ["dragon hunter"] = new("dragon_hunter"),
        ["prot"] = new("protection"),
        ["crit"] = new("critical"),
        ["sharp"] = new("sharpness"),
        ["ff"] = new("feather_falling"),
        ["rejuv"] = new("rejuvenate"),
    };

    /// <summary>A shorthand for a filter</summary>
    public record Alias(string Filter, string Value = null, string Default = null);

    private static readonly Regex GluedLetterDigit = new("^([a-z]+)(\\d+)$", RegexOptions.Compiled);
    private static readonly Regex GluedDigitLetter = new("^(\\d+)([a-z]+)$", RegexOptions.Compiled);
    private static readonly Regex NumberLike = new("^(\\d+|\\d+-\\d+|[<>]=?\\d+)$", RegexOptions.Compiled);
    private static readonly Regex PlainNumber = new("^\\d+$", RegexOptions.Compiled);
    private static readonly Regex RomanNumber = new("^[ivx]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LettersOnly = new("^[a-z]+$", RegexOptions.Compiled);
    private static readonly Regex CamelSplit = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new() { "of", "the", "to", "in", "on", "an", "as", "at", "is", "it", "or", "by", "for", "and" };
    private static readonly Dictionary<string, int> MaxValues = new(StringComparer.OrdinalIgnoreCase) { ["PetLevel"] = 100, ["Stars"] = 5 };

    /// <summary>
    /// Creates a new instance of <see cref="FilterQueryParser"/>
    /// </summary>
    public FilterQueryParser(FilterEngine engine, ILogger<FilterQueryParser> logger = null)
    {
        this.engine = engine;
        this.logger = logger;
    }

    private enum Kind { None, Enchant, Bool, Number, Option }

    private sealed class Info
    {
        public IFilter Filter;
        public string Name;
        public string Lower;
        public string Stripped;
        public string Squashed;
        public string[] Words;
        public Kind Kind;
        public List<string> Options = new();
        public int Max;
        public string BoolTrue = "true";
        public string Initials;
        public string StrippedInitials;
    }

    private sealed class Index
    {
        public int Count;
        public List<Info> All = new();
        public List<Info> Enchants = new();
        public Dictionary<string, Info> ByName = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Alias> Aliases = new();
        public HashSet<string> ExactOptionWords = new();
        public Info Rarity;
        public Info Reforge;
    }

    private sealed record Match(Info Info, string Value, int Consumed);

    private Index GetIndex()
    {
        var count = engine.AvailableFilters.Count();
        var current = index;
        if (current != null && current.Count == count)
            return current;
        lock (indexLock)
        {
            if (index == null || index.Count != count)
                index = BuildIndex(count);
            return index;
        }
    }

    private Index BuildIndex(int count)
    {
        var ix = new Index { Count = count };
        var empty = new OptionValues(new Dictionary<string, List<string>>());
        foreach (var f in engine.AvailableFilters.ToList())
        {
            try
            {
                if (string.IsNullOrEmpty(f.Name) || ix.ByName.ContainsKey(f.Name))
                    continue;
                var info = new Info
                {
                    Filter = f,
                    Name = f.Name,
                    Lower = f.Name.ToLowerInvariant(),
                    Words = SplitWords(f.Name)
                };
                info.Squashed = string.Concat(info.Words);
                info.Stripped = info.Lower.StartsWith("ultimate_") ? info.Lower.Substring(9) : info.Lower;
                var parts = info.Lower.Split('_');
                info.Initials = string.Concat(parts.Select(p => p.Length > 0 ? p[0] : ' '));
                info.StrippedInitials = string.Concat(info.Stripped.Split('_').Select(p => p.Length > 0 ? p[0] : ' '));
                var type = f.FilterType;
                if (f is EnchantBaseFilter)
                {
                    info.Kind = Kind.Enchant;
                    try
                    {
                        info.Max = f.OptionsGet(empty).Select(o => Convert.ToInt32(o)).DefaultIfEmpty(0).Max();
                    }
                    catch (Exception) { info.Max = 0; }
                    ix.Enchants.Add(info);
                }
                else
                {
                    if (info.Name == "Rarity")
                        info.Options = Enum.GetValues<Tier>().Where(t => t != Tier.UNKNOWN).Select(t => t.ToString()).ToList();
                    else if (info.Name == "Reforge")
                        info.Options = Enum.GetValues<ItemReferences.Reforge>()
                            .Where(r => r != ItemReferences.Reforge.None && r != ItemReferences.Reforge.Any).Select(r => r.ToString()).ToList();
                    else if ((type & FilterType.BOOLEAN) != 0 || ((type & (FilterType.NUMERICAL | FilterType.RANGE)) == 0 && (type & FilterType.Equal) != 0))
                    {
                        try
                        {
                            info.Options = f.OptionsGet(empty).Select(o => o?.ToString()).Where(o => !string.IsNullOrEmpty(o)).Distinct().Take(500).ToList();
                        }
                        catch (Exception) { /* needs modifier data to list options */ }
                    }
                    if ((type & FilterType.BOOLEAN) != 0)
                    {
                        info.Kind = Kind.Bool;
                        if (!info.Options.Contains("true", StringComparer.OrdinalIgnoreCase) && info.Options.Contains("yes", StringComparer.OrdinalIgnoreCase))
                            info.BoolTrue = "yes";
                    }
                    else if ((type & (FilterType.NUMERICAL | FilterType.RANGE)) != 0)
                        info.Kind = Kind.Number;
                    else if ((type & FilterType.Equal) != 0 && info.Options.Count > 0)
                        info.Kind = Kind.Option;
                }
                if (MaxValues.TryGetValue(info.Name, out var max))
                    info.Max = max;
                ix.ByName[info.Name] = info;
                ix.All.Add(info);
                if (info.Name == "Rarity") ix.Rarity = info;
                if (info.Name == "Reforge") ix.Reforge = info;
            }
            catch (Exception e)
            {
                logger?.LogDebug(e, "could not index filter {name}", f?.Name);
            }
        }
        foreach (var info in new[] { ix.Rarity, ix.Reforge }.Where(i => i != null))
            foreach (var o in info.Options.Where(o => o.Length >= 3))
                ix.ExactOptionWords.Add(o.ToLowerInvariant());
        foreach (var (key, alias) in Aliases)
        {
            if (ix.ByName.ContainsKey(alias.Filter))
                ix.Aliases[key] = alias;
            else
                logger?.LogWarning("Search alias {alias} points to unknown filter {filter}, skipping", key, alias.Filter);
        }
        return ix;
    }

    private static string[] SplitWords(string name)
        => name.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => CamelSplit.Split(p)).Select(p => p.ToLowerInvariant()).ToArray();

    /// <summary>
    /// Parses lowercase words greedily from left to right
    /// </summary>
    public ParsedFilters Parse(IEnumerable<string> words) => Parse(words, null);

    /// <summary>
    /// Parses lowercase words greedily from left to right, with <paramref name="options"/> being non null the stricter search mode is used
    /// </summary>
    public ParsedFilters Parse(IEnumerable<string> words, FilterParseOptions options)
    {
        var ix = GetIndex();
        var w = Normalize(words);
        var filters = new Dictionary<string, string>();
        var descriptions = new List<string>();
        var unmatched = new List<string>();
        var i = 0;
        while (i < w.Count)
        {
            var m = MatchAt(ix, w, i, null, options);
            var consumed = 0;
            if (m != null)
                consumed = m.Consumed;
            else if (PlainNumber.IsMatch(w[i]) && i + 1 < w.Count)
            {
                m = MatchAt(ix, w, i + 1, w[i], options);
                consumed = m == null ? 0 : m.Consumed + 1;
            }
            if (m == null || filters.ContainsKey(m.Info.Name))
            {
                unmatched.Add(w[i]);
                i++;
                continue;
            }
            filters[m.Info.Name] = m.Value;
            descriptions.Add(Describe(m.Info, m.Value));
            i += consumed;
        }
        return new ParsedFilters(filters, unmatched.ToArray(), string.Join(", ", descriptions));
    }

    private static List<string> Normalize(IEnumerable<string> words)
    {
        var result = new List<string>();
        foreach (var raw in words)
        {
            var word = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(word))
                continue;
            var m = GluedLetterDigit.Match(word);
            if (!m.Success)
                m = GluedDigitLetter.Match(word);
            if (m.Success)
            {
                result.Add(m.Groups[1].Value);
                result.Add(m.Groups[2].Value);
            }
            else
                result.Add(word);
        }
        return result;
    }

    private Match MatchAt(Index ix, List<string> w, int i, string pre, FilterParseOptions o)
        => TryAlias(ix, w, i, pre, o)
            ?? TryUltimate(ix, w, i, pre, o)
            ?? TryEnchant(ix, w, i, pre, o)
            ?? TryFilterName(ix, w, i, pre, o)
            ?? (pre == null ? TryOptionValue(ix, w, i, o) : null);

    /// <summary>
    /// In search mode a match without explicit value must not consume item name words
    /// </summary>
    private static bool ConsumesNameWord(FilterParseOptions o, bool explicitValue, IEnumerable<string> consumed)
        => o?.IsNameWord != null && !explicitValue && consumed.SelectMany(c => c.Split('_')).Any(o.IsNameWord);

    private Match TryAlias(Index ix, List<string> w, int i, string pre, FilterParseOptions o)
    {
        for (var k = Math.Min(3, w.Count - i); k >= 1; k--)
        {
            var key = string.Join(' ', w.Skip(i).Take(k));
            if (!ix.Aliases.TryGetValue(key, out var alias))
                continue;
            var info = ix.ByName[alias.Filter];
            if (alias.Value != null)
            {
                if (pre != null)
                    continue;
                if (ConsumesNameWord(o, false, w.Skip(i).Take(k)))
                    continue;
                return new Match(info, alias.Value, k);
            }
            var resolved = ResolveValue(info, w, i + k, alias.Default, pre);
            if (resolved != null && !ConsumesNameWord(o, pre != null || resolved.Value.used > 0, w.Skip(i).Take(k)))
                return new Match(info, resolved.Value.value, k + resolved.Value.used);
        }
        return null;
    }

    private Match TryUltimate(Index ix, List<string> w, int i, string pre, FilterParseOptions o)
    {
        if (w[i] != "ult" && w[i] != "ultimate")
            return null;
        for (var kx = Math.Min(2, w.Count - i - 1); kx >= 1; kx--)
        {
            var spaced = string.Join(' ', w.Skip(i + 1).Take(kx));
            var underscored = spaced.Replace(' ', '_');
            Info info = null;
            if (ix.Aliases.TryGetValue(spaced, out var alias) && alias.Filter.StartsWith("ultimate_"))
                info = ix.ByName[alias.Filter];
            info ??= ix.ByName.GetValueOrDefault("ultimate_" + underscored);
            if (info == null && underscored.Length >= 3)
                info = ix.Enchants.FirstOrDefault(e => e.Lower.StartsWith("ultimate_" + underscored));
            if (info == null)
                continue;
            var resolved = ResolveValue(info, w, i + 1 + kx, null, pre);
            if (resolved != null && !ConsumesNameWord(o, pre != null || resolved.Value.used > 0, w.Skip(i + 1).Take(kx)))
                return new Match(info, resolved.Value.value, 1 + kx + resolved.Value.used);
        }
        return null;
    }

    private Match TryEnchant(Index ix, List<string> w, int i, string pre, FilterParseOptions o)
    {
        for (var k = Math.Min(2, w.Count - i); k >= 1; k--)
        {
            var c = string.Join('_', w.Skip(i).Take(k));
            if (!LettersOnlyWithUnderscore(c))
                continue;
            var passes = new List<Func<Info, bool>> { e => e.Lower == c || e.Stripped == c };
            if (k == 1 && c.Length >= 2 && !StopWords.Contains(c))
                passes.Add(e => e.Initials == c || e.StrippedInitials == c);
            if (c.Length >= 3 && !ix.ExactOptionWords.Contains(c))
                passes.Add(e => e.Lower.StartsWith(c) || e.Stripped.StartsWith(c));
            for (var p = 0; p < passes.Count; p++)
                foreach (var e in ix.Enchants.Where(passes[p]))
                {
                    var resolved = ResolveValue(e, w, i + k, null, pre);
                    if (resolved == null)
                        continue;
                    var explicitValue = pre != null || resolved.Value.used > 0;
                    // implicit ">0": only the exact enchant name, and never a word that is part of an item name
                    if (!explicitValue && o is { RequireExplicitValues: true } && p > 0)
                        continue;
                    if (ConsumesNameWord(o, explicitValue, w.Skip(i).Take(k)))
                        continue;
                    return new Match(e, resolved.Value.value, k + resolved.Value.used);
                }
        }
        return null;
    }

    private static bool LettersOnlyWithUnderscore(string c) => LettersOnly.IsMatch(c.Replace("_", ""));

    private Match TryFilterName(Index ix, List<string> w, int i, string pre, FilterParseOptions o)
    {
        for (var k = Math.Min(3, w.Count - i); k >= 1; k--)
        {
            var c = string.Concat(w.Skip(i).Take(k));
            if (c.Length < 3 || !LettersOnly.IsMatch(c))
                continue;
            foreach (var info in ix.All)
            {
                if (info.Kind == Kind.None || info.Kind == Kind.Enchant || !info.Squashed.StartsWith(c))
                    continue;
                var resolved = ResolveValue(info, w, i + k, null, pre);
                if (resolved == null)
                    continue;
                var explicitValue = pre != null || resolved.Value.used > 0;
                if (!explicitValue && o is { RequireExplicitValues: true } && info.Squashed != c)
                    continue;
                if (ConsumesNameWord(o, explicitValue, w.Skip(i).Take(k)))
                    continue;
                return new Match(info, resolved.Value.value, k + resolved.Value.used);
            }
        }
        return null;
    }

    private Match TryOptionValue(Index ix, List<string> w, int i, FilterParseOptions o)
    {
        var word = w[i];
        if (word.Length < 3 || !LettersOnly.IsMatch(word))
            return null;
        var targets = new[] { ix.Rarity, ix.Reforge }.Where(t => t != null).ToList();
        foreach (var t in targets)
        {
            var exact = t.Options.FirstOrDefault(o => o.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (exact == null)
                continue;
            // rarity words always stay filters, reforges that are also item name words ("superior") do not
            if (t != ix.Rarity && ConsumesNameWord(o, false, new[] { word }))
                continue;
            return new Match(t, exact, 1);
        }
        if (o is { RequireExplicitValues: true })
            return null; // no prefix guessing ("wither" is not "withered")
        foreach (var t in targets)
        {
            var matches = t.Options.Where(o => o.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (matches.Count == 1)
                return new Match(t, matches[0], 1);
        }
        return null;
    }

    private static (string value, int used)? ResolveValue(Info info, List<string> w, int at, string def, string pre)
    {
        if (pre != null)
            return info.Kind is Kind.Enchant or Kind.Number ? (pre, 0) : null;
        var next = at < w.Count ? w[at] : null;
        switch (info.Kind)
        {
            case Kind.Enchant:
            case Kind.Number:
                var number = next == null ? null : NumberToken(info, next);
                if (number != null)
                    return (number, 1);
                if (def != null)
                    return (def, 0);
                return info.Kind == Kind.Enchant ? (">0", 0) : null;
            case Kind.Bool:
                if (next is "no" or "false")
                    return ("false", 1);
                if (next is "yes" or "true")
                    return (info.BoolTrue, 1);
                return (info.BoolTrue, 0);
            case Kind.Option:
                if (next != null && next.Length >= 2)
                {
                    var exact = info.Options.FirstOrDefault(o => o.Equals(next, StringComparison.OrdinalIgnoreCase))
                        ?? info.Options.FirstOrDefault(o => o.StartsWith(next, StringComparison.OrdinalIgnoreCase));
                    if (exact != null)
                        return (exact, 1);
                }
                return def != null ? (def, 0) : null;
        }
        return null;
    }

    private static string NumberToken(Info info, string word)
    {
        if (NumberLike.IsMatch(word))
            return word;
        if (word == "max")
            return info.Kind == Kind.Enchant ? (info.Max > 0 ? info.Max : 10).ToString() : info.Max > 0 ? info.Max.ToString() : null;
        if (RomanNumber.IsMatch(word))
            return Roman.From(word.ToUpperInvariant()).ToString();
        return null;
    }

    private static string Title(IEnumerable<string> words)
        => string.Join(' ', words.Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));

    private static string Describe(Info info, string value)
    {
        var name = Title(info.Kind == Kind.Enchant ? info.Lower.Split('_') : info.Words);
        switch (info.Kind)
        {
            case Kind.Enchant:
                return value == ">0" ? name : $"{name} {value}";
            case Kind.Bool:
                return value is "false" or "no" ? "Not " + name : name;
            case Kind.Option when info.Name is "Rarity" or "Reforge":
                return Title(value.Split('_'));
            default:
                return $"{name} {value}";
        }
    }

    /// <summary>
    /// Filter names, shorthands and option values that start with the partial word (for autocomplete)
    /// </summary>
    public IEnumerable<string> Suggest(string partialWord, int max)
    {
        if (string.IsNullOrWhiteSpace(partialWord) || max <= 0)
            return Enumerable.Empty<string>();
        var ix = GetIndex();
        var p = partialWord.Trim().ToLowerInvariant();
        bool Starts(string s) => s.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        return ix.All.Select(i => i.Name)
            .Concat(ix.Enchants.Select(e => e.Stripped))
            .Concat(ix.Aliases.Keys)
            .Concat(ix.All.SelectMany(i => i.Name is "Rarity" or "Reforge" ? i.Options : Enumerable.Empty<string>()))
            .Where(Starts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// Creates the item page id/url query for an item with filters, eg <c>HYPERION?sharpness=7&amp;Reforge=Heroic</c>
    /// </summary>
    public static string FormatId(string tag, Dictionary<string, string> filters)
    {
        if (filters == null || filters.Count == 0)
            return tag;
        return tag + "?" + string.Join('&', filters.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value ?? "")}"));
    }
}
