using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.Commands.Shared.Goals;

/// <summary>
/// Completes Sell steps from the chat lines the game prints when an auction is listed or a bazaar
/// sell offer is placed or filled. Pure, works on the persisted goal state only.
/// </summary>
public static class GoalChatProgress
{
    private static readonly Regex AuctionLine = new(@"^(?:BIN )?Auction started for (?<name>.+?)!?$", RegexOptions.Compiled);
    private static readonly Regex SellOfferLine = new(@"^\[Bazaar\] Sell Offer Setup! (?<n>[\d,]+)x (?<name>.+?) for [\d,.]+ coins\.?$", RegexOptions.Compiled);
    private static readonly Regex SoldLine = new(@"^\[Bazaar\] Sold (?<n>[\d,]+)x (?<name>.+?) for [\d,.]+ coins!?$", RegexOptions.Compiled);
    private static readonly Regex Codes = new("§.", RegexOptions.Compiled);
    private static readonly Regex Multiplier = new(@"\s*×\d+$", RegexOptions.Compiled);

    /// <summary>Parses a chat line, false when it does not confirm a listing or sale.</summary>
    public static bool TryParse(string line, out string itemName, out long amount, out bool isBazaar)
    {
        itemName = null;
        amount = 0;
        isBazaar = false;
        line = Codes.Replace(line ?? "", "").Trim();
        var auction = AuctionLine.Match(line);
        if (auction.Success)
        {
            itemName = auction.Groups["name"].Value;
            amount = 1;
            return true;
        }
        var bazaar = SellOfferLine.Match(line);
        if (!bazaar.Success)
            bazaar = SoldLine.Match(line);
        if (!bazaar.Success || !long.TryParse(bazaar.Groups["n"].Value.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out amount))
            return false;
        itemName = bazaar.Groups["name"].Value;
        isBazaar = true;
        return true;
    }

    /// <summary>
    /// Adds the amount of a confirming chat line to the Sell step of one goal (the focused goal first) whose
    /// crafting is done and whose item matches, and completes the step once everything is sold.
    /// Returns true when the list changed.
    /// </summary>
    public static bool Apply(GoalList list, string line, Func<string, string> nameOfTag, DateTime now)
    {
        if (list?.Goals == null || !TryParse(line, out var chatName, out var amount, out _))
            return false;
        var chat = Normalize(chatName);
        var ordered = list.Goals.Where(g => g.Id == list.FocusId).Concat(list.Goals.Where(g => g.Id != list.FocusId));
        foreach (var goal in ordered)
        {
            var step = goal.Root;
            if (goal.BazaarFlip != null || step is not { Kind: StepKind.Sell } || step.DoneAt.HasValue || !step.Children.All(c => c.DoneAt.HasValue))
                continue;
            var name = Normalize(nameOfTag?.Invoke(step.ItemTag));
            if (name.Length == 0)
                name = Normalize(Multiplier.Replace(goal.Title ?? "", ""));
            if (name.Length == 0 || !chat.Contains(name, StringComparison.Ordinal))
                continue;
            step.Sold += amount;
            if (step.Sold >= step.Quantity)
                step.DoneAt = now;
            return true;
        }
        return false;
    }

    private static string Normalize(string name)
    {
        var cleaned = new string(Codes.Replace(name ?? "", "").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '\'').ToArray());
        return Regex.Replace(cleaned, @"\s+", " ").Trim().ToLowerInvariant();
    }
}
