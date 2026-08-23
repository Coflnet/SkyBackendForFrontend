using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Runs the real calculator against live prices and prints the answers. Explicit because it reaches
/// the network; this is the harness the published figures come from, so the numbers in an article and
/// the numbers the endpoint serves are produced by the same code.
/// </summary>
[Explicit("hits the live bazaar and hypixel resource apis")]
public class MinionRankingHarness
{
    private const string SpreadUrl = "https://sky.coflnet.com/api/flip/bazaar/spread";
    private const string PriceUrl = "https://sky.coflnet.com/api/item/price/";
    private const string ItemsUrl = "https://api.hypixel.net/v2/resources/skyblock/items";
    private const string RecipeUrl = "https://sky.coflnet.com/api/craft/recipe/";

    /// <summary>Every tag the minion table touches, plus the enchanted form a compactor would produce.</summary>
    private static HashSet<string> NeededTags(IEnumerable<MinionService.Minion> minions)
    {
        var tags = new HashSet<string>();
        foreach (var minion in minions)
        {
            foreach (var product in minion.Products)
                if (product.Tag != null)
                {
                    tags.Add(product.Tag);
                    tags.Add("ENCHANTED_" + product.Tag);
                }
            foreach (var ingredient in minion.Upgrade.SelectMany(step => step))
                if (ingredient?.Tag != null)
                    tags.Add(ingredient.Tag);
        }
        return tags;
    }

    /// <summary>
    /// One request, retried when the api rate limits. Fetching these concurrently returns 429 for two
    /// thirds of them, and a swallowed 429 is indistinguishable from "this item has no price".
    /// </summary>
    private static async Task<string> Backoff(HttpClient http, string url)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await http.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(TimeSpan.FromSeconds(1 << attempt));
                continue;
            }
            if (!response.IsSuccessStatusCode)
                return null;
            // A 200 with an empty body happens for tags the api knows nothing about.
            var body = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        return null;
    }

    /// <summary>-1 marks an empty side of the order book, not a negative price.</summary>
    private static MinionItemPrice Sides(double buy, double sell)
        => new(buy < 0 ? 0 : buy, sell < 0 ? 0 : sell);

    private static async Task<(Dictionary<string, MinionItemPrice>, Dictionary<string, double>, Dictionary<string, MinionCompaction>)>
        Market(IEnumerable<MinionService.Minion> minions)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // The api answers an anonymous client with 403, which silently emptied the price map.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("coflnet-minion-harness/1.0");

        var wanted = NeededTags(minions);
        var prices = new Dictionary<string, MinionItemPrice>();

        // One bulk call covers all but a handful. The per-item endpoint rate limits hard enough that
        // fetching every tag that way returns 429 for two thirds of them, which reads as "no price".
        using (var spread = JsonDocument.Parse(await http.GetStringAsync(SpreadUrl)))
            foreach (var entry in spread.RootElement.EnumerateArray())
            {
                var flip = entry.GetProperty("flip");
                if (!flip.TryGetProperty("buyPrice", out var buy) || !flip.TryGetProperty("sellPrice", out var sell))
                    continue;
                var tag = flip.GetProperty("itemTag").GetString();
                var price = Sides(buy.GetDouble(), sell.GetDouble());
                if (price.InstaBuy > 0 || price.InstaSell > 0)
                    prices[tag] = price;
            }

        // Top up the rest one at a time, backing off when the api says to.
        foreach (var tag in wanted.Where(t => !prices.ContainsKey(t)))
        {
            var body = await Backoff(http, PriceUrl + Uri.EscapeDataString(tag) + "/current");
            if (body == null)
                continue;
            using var json = JsonDocument.Parse(body);
            // An unknown tag answers with zeros and available -1; that is absence, not a price.
            if (json.RootElement.TryGetProperty("available", out var available) && available.GetDouble() == -1)
                continue;
            var price = Sides(
                json.RootElement.GetProperty("buy").GetDouble(),
                json.RootElement.GetProperty("sell").GetDouble());
            if (price.InstaBuy > 0 || price.InstaSell > 0)
                prices[tag] = price;
        }

        var experience = new Dictionary<string, double>();
        using (var items = JsonDocument.Parse(await http.GetStringAsync(ItemsUrl)))
            foreach (var item in items.RootElement.GetProperty("items").EnumerateArray())
            {
                if (!item.TryGetProperty("experience", out var skills))
                    continue;
                double perItem = 0;
                foreach (var skill in skills.EnumerateObject())
                    if (skill.Value.TryGetProperty("MINION_STORAGE", out var amount))
                        perItem += amount.GetDouble();
                if (perItem > 0)
                    experience[item.GetProperty("id").GetString()] = perItem;
            }

        // Ratios come from the recipes rather than a flat 160, which is wrong for every second-tier
        // enchanted item and silently inflates those minions by orders of magnitude.
        var recipes = new Dictionary<string, Dictionary<string, double>>();
        foreach (var tag in prices.Keys.Where(t => t.StartsWith("ENCHANTED_")).OrderBy(t => t))
        {
            var grid = await Backoff(http, RecipeUrl + Uri.EscapeDataString(tag));
            if (grid == null)
                continue;
            using var json = JsonDocument.Parse(grid);
            var slots = json.RootElement.EnumerateObject()
                .Where(field => field.Name != "count" && field.Value.ValueKind == JsonValueKind.String)
                .Select(field => field.Value.GetString());
            var totals = MinionCalculator.ParseGrid(slots);
            if (totals.Count > 0)
                recipes[tag] = totals;
        }
        var productTags = minions.SelectMany(m => m.Products).Where(p => p.Tag != null).Select(p => p.Tag);
        var compaction = MinionCalculator.ResolveCompaction(recipes, productTags);
        Console.WriteLine($"read {recipes.Count} enchanted recipes");

        var uncovered = wanted.Where(t => !prices.ContainsKey(t) && !t.StartsWith("ENCHANTED_")).OrderBy(t => t).ToList();
        Console.WriteLine($"no price for {uncovered.Count} of the tags the table uses: {string.Join(", ", uncovered)}");

        return (prices, experience, compaction);
    }

    private static void Print(string heading, string caveat, List<MinionOutlook> ranked, bool experience = false)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {heading} ===");
        Console.WriteLine(caveat);
        foreach (var o in ranked.Take(12))
        {
            var headline = experience
                ? $"{o.ExperiencePerDay,12:N0} xp/day"
                : $"{o.CoinsPerDay,14:N0} coins/day";
            Console.WriteLine(
                $"  {o.Name,-22} {("T" + o.Tier),-4} {headline}  setup {o.SetupCost,13:N0}" +
                $"  payback {(double.IsInfinity(o.PaybackDays) ? "never" : o.PaybackDays.ToString("N1") + "d"),8}" +
                $"  fills in {o.SecondsToFill / 3600,7:N1}h{(o.Compacted ? "  compacted" : "")}" +
                $"{(o.MissingRequirements.Count > 0 ? "  needs " + string.Join(", ", o.MissingRequirements) : "")}");
        }
    }

    [Test]
    public async Task PrintTheAnswers()
    {
        var minions = new MinionService().MinionData.Values.ToList();
        var (prices, experience, compaction) = await Market(minions);
        var calculator = new MinionCalculator(prices, experience, compaction);

        Console.WriteLine($"live bazaar prices for {prices.Count} items, minion experience for {experience.Count}, "
            + $"compaction steps for {compaction.Count}, read {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

        Print("a) collected daily, sell offers, no compactor",
            "Raw items regularly beat their enchanted form, so compaction is off here.",
            calculator.Rank(minions, new MinionQuery
            {
                OfflineSeconds = 24 * 3600,
                AllowCompaction = false,
                Sell = MinionSellMode.SellOffer
            }));

        Print("a2) collected daily, compactor allowed",
            "Same day, with the calculator free to compact where that pays more.",
            calculator.Rank(minions, new MinionQuery { OfflineSeconds = 24 * 3600 }));

        Print("b-coins) one Derpy term (~5 days), doubled output",
            "TURBO MINIONS doubles output, which also halves the time to fill.",
            calculator.Rank(minions, new MinionQuery
            {
                OfflineSeconds = 5 * 24 * 3600,
                OutputMultiplier = 2
            }));

        Print("b-xp) one Derpy term, ranked on skill experience",
            "MOAR SKILLZ adds half again on top of the doubled output.",
            calculator.Rank(minions, new MinionQuery
            {
                OfflineSeconds = 5 * 24 * 3600,
                OutputMultiplier = 2,
                ExperienceMultiplier = 1.5,
                Objective = MinionObjective.Experience
            }), experience: true);

        Print("c) left alone for a year",
            "Only an infinite fuel is modelled (+40% Everburning Flame); storage decides everything.",
            calculator.Rank(minions, new MinionQuery
            {
                OfflineSeconds = 365 * 24 * 3600,
                SpeedBoost = 0.40
            }));

        Print("c2) left alone for a year with an Enchanted Hopper",
            "A hopper ships overflow at 70% of npc value instead of letting the minion stall.",
            calculator.Rank(minions, new MinionQuery
            {
                OfflineSeconds = 365 * 24 * 3600,
                SpeedBoost = 0.40,
                HopperNpcShare = 0.7
            }));

        Print("budget) daily collection on a 1,000,000 coin budget",
            "The tier is whatever that budget reaches, so this is a different list.",
            calculator.Rank(minions, new MinionQuery { OfflineSeconds = 24 * 3600, Budget = 1_000_000 }));
    }
}
