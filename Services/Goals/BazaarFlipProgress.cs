using System;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Commands.Shared.Goals;

/// <summary>Persisted evidence for a manually guided Bazaar flip. Inventory alone never proves a sale or claim.</summary>
public class BazaarFlipState
{
    public List<BazaarFlipOrder> Orders { get; set; } = new();
    [Newtonsoft.Json.JsonIgnore]
    public long BuyClaimed => Orders.Where(o => !o.IsSell).Sum(o => o.Claimed);
    [Newtonsoft.Json.JsonIgnore]
    public long SellClaimed => Orders.Where(o => o.IsSell).Sum(o => o.Claimed);
    public long Revision { get; set; }
    // Never adopt an earlier sale retrospectively after new purchases appear.
    public HashSet<DateTime> IgnoredSellOrders { get; set; } = new();
}

/// <summary>Order identity and confirmed quantities retained even after cancellation or removal.</summary>
public class BazaarFlipOrder
{
    public DateTime CreatedAt { get; set; }
    public bool IsSell { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long Amount { get; set; }
    public long Filled { get; set; }
    public long Claimed { get; set; }
    public bool Active { get; set; }
}

public enum BazaarFlipAction { Buy, WaitBuy, ClaimItems, Sell, WaitSell, ClaimCoins, Done }

public static class BazaarFlipProgress
{
    public static long Filled(BazaarFlipState state, bool sell) => state.Orders.Where(o => o.IsSell == sell).Sum(o => o.Filled);

    public static long Committed(BazaarFlipState state, bool sell) => state.Orders.Where(o => o.IsSell == sell).Sum(o => o.Active ? o.Amount : o.Filled);

    public static BazaarFlipAction Next(Goal goal)
    {
        var s = goal.BazaarFlip;
        if (s.BuyClaimed >= goal.Quantity && s.SellClaimed >= goal.Quantity) return BazaarFlipAction.Done;
        if (Filled(s, true) > s.SellClaimed) return BazaarFlipAction.ClaimCoins;
        if (s.BuyClaimed > Committed(s, true)) return BazaarFlipAction.Sell;
        if (Filled(s, false) > s.BuyClaimed) return BazaarFlipAction.ClaimItems;
        if (Committed(s, false) < goal.Quantity) return BazaarFlipAction.Buy;
        return Committed(s, true) > s.SellClaimed ? BazaarFlipAction.WaitSell : BazaarFlipAction.WaitBuy;
    }

    /// <summary>Apply an owner-filtered, monotonically versioned snapshot. Missing orders prove closure, never a claim.</summary>
    public static bool Observe(Goal goal, IEnumerable<BazaarFlipOrder> observations, long revision)
    {
        var state = goal.BazaarFlip;
        if (state == null || revision <= state.Revision) return false;
        state.Revision = revision;
        var knownClaims = state.Orders.Where(o => !o.IsSell).ToDictionary(o => o.CreatedAt,
            o => (o.Claimed, FullyClaimed: o.Claimed >= o.Amount, o.ClosedAt));
        var created = new DateTime(goal.CreatedAt.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, goal.CreatedAt.Kind);
        var orders = observations.Where(o => o.CreatedAt >= created).OrderBy(o => o.IsSell).ThenBy(o => o.CreatedAt).ToList();
        var changed = false;
        foreach (var stored in state.Orders.Where(o => o.Active
            && !orders.Any(n => n.CreatedAt == o.CreatedAt && n.IsSell == o.IsSell)))
        {
            stored.Active = false;
            changed = true;
        }
        foreach (var order in orders)
        {
            var stored = state.Orders.FirstOrDefault(o => o.CreatedAt == order.CreatedAt && o.IsSell == order.IsSell);
            // ClosedAt bounds the latest retained claim evidence; new partial claims remain ambiguous.
            // Otherwise only claims persisted before this snapshot can finance a newly observed sale.
            var target = order.IsSell ? Math.Min(goal.Quantity,
                state.Orders.Where(o => !o.IsSell && o.CreatedAt <= order.CreatedAt).Sum(o =>
                {
                    if (o.Claimed >= o.Amount && o.ClosedAt.HasValue && o.ClosedAt <= order.CreatedAt)
                        return o.Claimed;
                    return knownClaims.TryGetValue(o.CreatedAt, out var known)
                        && !(known.FullyClaimed && known.ClosedAt > order.CreatedAt) ? known.Claimed : 0;
                })) : goal.Quantity;
            if (stored == null && order.IsSell && (state.IgnoredSellOrders.Contains(order.CreatedAt) || target == 0))
            {
                changed |= state.IgnoredSellOrders.Add(order.CreatedAt);
                continue;
            }
            // Later acquisitions cannot increase the quantity attributed to an earlier order.
            var amount = stored?.Amount ?? Math.Min(order.Amount, target - Committed(state, order.IsSell));
            if (amount <= 0) continue;
            if (stored == null)
            {
                stored = new BazaarFlipOrder { CreatedAt = order.CreatedAt, IsSell = order.IsSell };
                state.Orders.Add(stored);
                changed = true;
            }
            var filled = Math.Max(stored.Filled, Math.Min(amount, Math.Max(order.Filled, order.Claimed)));
            var claimed = Math.Max(stored.Claimed, Math.Min(amount, order.Claimed));
            var active = order.Active && claimed < amount;
            var closedAt = stored.ClosedAt == null || order.ClosedAt > stored.ClosedAt ? order.ClosedAt : stored.ClosedAt;
            changed |= stored.Amount != amount || stored.Filled != filled || stored.Claimed != claimed
                || stored.Active != active || stored.ClosedAt != closedAt;
            stored.Amount = amount;
            stored.Filled = filled;
            stored.Claimed = claimed;
            stored.Active = active;
            stored.ClosedAt = closedAt;
        }
        return changed;
    }

    public static ChatPartDto Line(Goal goal, bool hasCookie, string name, string search)
    {
        var s = goal.BazaarFlip;
        var action = Next(goal);
        var buyAmount = Math.Max(0, goal.Quantity - Committed(s, false));
        var sellAmount = Math.Max(0, s.BuyClaimed - Committed(s, true));
        var text = action switch {
            BazaarFlipAction.Buy => $"§aPlace buy order: {buyAmount:N0}x {name}",
            BazaarFlipAction.WaitBuy => $"§eWaiting for buy fills: {Filled(s, false):N0}/{goal.Quantity:N0}x {name}",
            BazaarFlipAction.ClaimItems => $"§aClaim bought items: {Filled(s, false) - s.BuyClaimed:N0}x {name}",
            BazaarFlipAction.Sell => $"§aCreate sell offer: {sellAmount:N0}x {name}",
            BazaarFlipAction.WaitSell => $"§eWaiting for sell fills: {Filled(s, true):N0}/{goal.Quantity:N0}x {name}",
            BazaarFlipAction.ClaimCoins => $"§aClaim coins for {Filled(s, true) - s.SellClaimed:N0}x {name}",
            _ => $"§aBazaar flip complete: {goal.Quantity:N0}x {name} claimed"
        };
        var command = action switch {
            BazaarFlipAction.Buy => $"/cofl bzorder {(hasCookie ? "" : "search ")}{buyAmount} {search}",
            BazaarFlipAction.Sell => hasCookie ? $"/bz {search}" : $"/cofl bazaarsearch {search}",
            BazaarFlipAction.Done => null,
            _ => hasCookie ? "/managebazaarorders" : null
        };
        return new ChatPartDto { Text = text, OnClick = command,
            Hover = action == BazaarFlipAction.Buy ? "Choose Create Buy Order and review the current price before confirming." + (hasCookie ? "" : " Open a Bazaar NPC first.")
                : action == BazaarFlipAction.Sell ? $"Choose Create Sell Offer for {sellAmount:N0} items and review the current price. Partial claims can be sold while the rest fills." + (hasCookie ? "" : " Open a Bazaar NPC first.")
                : action == BazaarFlipAction.Done ? "The sell proceeds were claimed; listing or filling alone does not complete this flip."
                : "Open Your Bazaar Orders to inspect or claim this item. Cancelling unfilled quantities allows replacement orders." + (hasCookie ? "" : " Talk to a Bazaar NPC first.") };
    }
}
