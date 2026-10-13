using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Coflnet.Sky.Commands.Shared.Goals;

/// <summary>Payload of the mod's infoDisplay message (camelCase json).</summary>
[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InfoDisplayDto
{
    public int Id { get; set; } = 3;
    public string Title { get; set; }
    public List<ChatPartDto> Lines { get; set; } = new();
    public int Ttl { get; set; } = 600;
    public bool Clear { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class ChatPartDto
{
    public string Text { get; set; }
    public string Hover { get; set; }
    public string OnClick { get; set; }
}

public static class GoalHud
{
    public static InfoDisplayDto BuildDisplay(GoalList list, IReadOnlyDictionary<string, GoalProgress> progressByGoalId,
        bool hasCookie, Func<string, string> nameLookup, Func<string, string> searchNameLookup,
        IReadOnlyDictionary<string, AuctionOffer> offerByTag, Func<string, bool> isBazaarItem = null)
    {
        var goals = list?.Goals ?? new();
        if (goals.Count == 0)
            return new InfoDisplayDto { Clear = true };
        var focus = goals.FirstOrDefault(g => g.Id == list.FocusId) ?? goals[0];
        var dto = new InfoDisplayDto();
        var focusProgress = Get(progressByGoalId, focus.Id);
        dto.Title = $"§6§lGoal: §f{focus.Title} §7{focusProgress?.Done ?? 0}/{focusProgress?.Total ?? 0} steps";
        if (focusProgress != null)
        {
            var steps = MergeSameItem(focusProgress.Actionable);
            // auctions can be sniped, so the other purchases only come after them
            var auctionFirst = steps.Any(s => s.Kind == StepKind.Auction);
            foreach (var step in steps)
                dto.Lines.Add(StepLine(step, hasCookie, nameLookup, searchNameLookup, offerByTag, auctionFirst, isBazaarItem));
        }
        if (focusProgress is { Complete: true })
            dto.Lines.Add(new ChatPartDto { Text = "§aGoal complete, all steps are done", Hover = "Nothing left to do for this goal" });
        foreach (var other in goals.Where(g => g != focus))
        {
            var p = Get(progressByGoalId, other.Id);
            dto.Lines.Add(new ChatPartDto
            {
                Text = $"§7▸ {other.Title} {p?.Done ?? 0}/{p?.Total ?? 0}",
                Hover = "Click to focus this goal",
                OnClick = $"/cofl goal focus {other.Id}"
            });
        }
        return dto;
    }

    /// <summary>
    /// Presents purchase steps for the same item as one step with the summed quantity.
    /// Merged entries are new instances (the persisted goal tree is never mutated), keeping the id of the first step.
    /// </summary>
    public static IReadOnlyList<GoalStep> MergeSameItem(IEnumerable<GoalStep> steps)
    {
        var result = new List<GoalStep>();
        var groups = new Dictionary<(StepKind, string), List<GoalStep>>();
        var slots = new List<(GoalStep single, List<GoalStep> group)>();
        foreach (var step in steps)
        {
            var mergeable = step.Kind is StepKind.BuyOrder or StepKind.InstaBuy or StepKind.Npc or StepKind.Auction && step.ItemTag != null;
            if (!mergeable)
            {
                slots.Add((step, null));
                continue;
            }
            if (groups.TryGetValue((step.Kind, step.ItemTag), out var group))
                group.Add(step);
            else
            {
                groups[(step.Kind, step.ItemTag)] = group = new List<GoalStep> { step };
                slots.Add((null, group));
            }
        }
        foreach (var (single, group) in slots)
        {
            if (group == null || group.Count == 1)
                result.Add(single ?? group[0]);
            else
                result.Add(new GoalStep
                {
                    Id = group[0].Id,
                    Kind = group[0].Kind,
                    ItemTag = group[0].ItemTag,
                    Note = group[0].Note,
                    Quantity = group.Sum(g => g.Quantity),
                    UnitPrice = GoalPlanner.WeightedPrice(group.Select(g => (g.Quantity, g.UnitPrice)))
                });
        }
        return result;
    }

    private static GoalProgress Get(IReadOnlyDictionary<string, GoalProgress> map, string id)
        => map != null && map.TryGetValue(id, out var p) ? p : null;

    private const string AuctionFirstHover = "Buy the auction item first, it can sell before you get to it.\n";

    /// <summary>Hover of lines that would need /ah without a booster cookie.</summary>
    public const string NoCookieAuctionHover = "Without a booster cookie /ah is not available. Talk to the Auction Master or an Auction Agent.";
    /// <summary>Hover of lines that would need /bz without a booster cookie.</summary>
    public const string NoCookieBazaarHover = "Without a booster cookie /bz is not available. Talk to a Bazaar NPC in the Hub.";
    /// <summary>Where to go to use the auction house without a booster cookie.</summary>
    public const string AtAuctionMaster = " §7at the Auction Master";
    /// <summary>Where to go to use the bazaar without a booster cookie.</summary>
    public const string AtBazaar = " §7at the Bazaar";

    private static ChatPartDto StepLine(GoalStep step, bool hasCookie, Func<string, string> nameLookup,
        Func<string, string> searchNameLookup, IReadOnlyDictionary<string, AuctionOffer> offers, bool auctionFirst,
        Func<string, bool> isBazaarItem)
    {
        var name = nameLookup(step.ItemTag) ?? step.ItemTag;
        var qty = step.Quantity;
        // purchases other than the auction wait while an auction step is pending
        var color = auctionFirst ? "§7then " : "§c";
        var hoverPrefix = auctionFirst ? AuctionFirstHover : "";
        switch (step.Kind)
        {
            case StepKind.BuyOrder:
            case StepKind.InstaBuy:
                var verb = step.Kind == StepKind.BuyOrder ? "Order" : "Buy";
                var search = searchNameLookup(step.ItemTag) ?? name;
                return new ChatPartDto
                {
                    Text = $"{color}{verb} {qty}x {name}",
                    Hover = hoverPrefix + (hasCookie ? $"Click to place a bazaar order for {qty}x {name}" : "Open the bazaar first, then click"),
                    OnClick = hasCookie ? $"/cofl bzorder {qty} {search}" : $"/cofl bzorder search {qty} {search}"
                };
            case StepKind.Auction:
                AuctionOffer offer = null;
                if (offers != null && step.ItemTag != null)
                    offers.TryGetValue(step.ItemTag, out offer);
                if (offer == null)
                    return hasCookie
                        ? new ChatPartDto { Text = $"§cBuy {qty}x {name} §7(auction)", Hover = "Click to open the auction house", OnClick = "/ah" }
                        : new ChatPartDto { Text = $"§cBuy {qty}x {name} §7(auction)" + AtAuctionMaster, Hover = NoCookieAuctionHover };
                var command = offer.ViewCommand();
                // /viewauction works without a booster cookie, /ah does not
                var canClick = command != null || hasCookie;
                var hover = command != null ? "Click to view the cheapest auction" : hasCookie ? "Click to open the auction house" : NoCookieAuctionHover;
                if (step.UnitPrice is { } planned)
                    hover += $"\nPlanned {ShortPrice(planned)}, cheapest now {ShortPrice(offer.Price)}";
                return new ChatPartDto
                {
                    Text = $"§cBuy {qty}x {name} §7(auction) §f{ShortPrice(offer.Price)}{ProfitNote(step, offer)}" + (canClick ? "" : AtAuctionMaster),
                    Hover = hover,
                    OnClick = command ?? (hasCookie ? "/ah" : null)
                };
            case StepKind.Npc:
                return new ChatPartDto
                {
                    Text = $"{color}Buy {qty}x {name} §7({(string.IsNullOrEmpty(step.Note) ? "NPC" : step.Note)})",
                    Hover = hoverPrefix + "Buy from an NPC"
                };
            case StepKind.Craft:
                return new ChatPartDto { Text = $"§aCraft {name}: click to open /recipe", Hover = "Click to open the recipe", OnClick = $"/recipe {name}" };
            case StepKind.Forge:
                return new ChatPartDto { Text = $"§aForge {name}", Hover = "Click to warp to the forge", OnClick = "/warp forge" };
            default:
                var bazaar = isBazaarItem?.Invoke(step.ItemTag) ?? false;
                if (!hasCookie)
                    return new ChatPartDto
                    {
                        Text = $"§aSell {name}" + (bazaar ? AtBazaar : AtAuctionMaster),
                        Hover = bazaar ? NoCookieBazaarHover : NoCookieAuctionHover
                    };
                return new ChatPartDto
                {
                    Text = $"§aSell {name}",
                    Hover = bazaar ? "Click to open the bazaar" : "Click to open the auction house",
                    OnClick = bazaar ? $"/bz {name}" : "/ah"
                };
        }
    }

    /// <summary>
    /// Difference between the offer and the planned price of a single item (positive: more expensive than planned),
    /// null when the plan has no price, more than one item is needed or the difference is below 0.5 %.
    /// </summary>
    public static double? PriceDifference(GoalStep step, AuctionOffer offer)
    {
        if (offer == null || step.Quantity != 1 || step.UnitPrice is not { } planned || planned <= 0)
            return null;
        var diff = offer.Price - planned;
        return Math.Abs(diff) >= planned * 0.005 ? diff : null;
    }

    private static string ProfitNote(GoalStep step, AuctionOffer offer)
        => PriceDifference(step, offer) switch
        {
            > 0 and var diff => $" §c-{ShortPrice(diff)} profit",
            < 0 and var diff => $" §a+{ShortPrice(-diff)} profit",
            _ => ""
        };

    /// <summary>Formats a coin amount with at most three significant digits and a k, m or b suffix (2.5m, 980k).</summary>
    public static string ShortPrice(double value)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);
        string[] suffixes = ["", "k", "m", "b"];
        var unit = 0;
        while (abs >= 999.5 && unit < suffixes.Length - 1)
        {
            abs /= 1000;
            unit++;
        }
        var scaled = abs < 10 ? Math.Round(abs, 2) : abs < 100 ? Math.Round(abs, 1) : Math.Round(abs);
        return sign + scaled.ToString("0.##", CultureInfo.InvariantCulture) + suffixes[unit];
    }

    public static string Hash(InfoDisplayDto dto)
    {
        var sb = new StringBuilder();
        sb.Append(dto.Title).Append('\n').Append(dto.Clear);
        foreach (var l in dto.Lines)
            sb.Append('\n').Append(l.Text).Append('|').Append(l.Hover).Append('|').Append(l.OnClick);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
