using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Coflnet.Sky.Commands.Shared.Goals;

/// <summary>Per user list of goals, stored under the settings key "goals".</summary>
public class GoalList
{
    public List<Goal> Goals { get; set; } = new();
    public string FocusId { get; set; }
}

public class Goal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; }
    public string RootItemTag { get; set; }
    public long Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>"website" or "mod"</summary>
    public string Source { get; set; }
    public GoalStep Root { get; set; }
    public BazaarFlipState BazaarFlip { get; set; }
    /// <summary>Planner metadata of a bazaar plan goal; null for legacy goals.</summary>
    public BazaarPlanInfo Plan { get; set; }
}

/// <summary>Quote and confidence metadata a bazaar plan goal was created from.</summary>
public class BazaarPlanInfo
{
    public string Mode { get; set; }
    public string QuoteId { get; set; }
    public string MarketFrameId { get; set; }
    public string PolicyId { get; set; }
    public int PolicyVersion { get; set; }
    public string ModelVersion { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long OriginalQuantity { get; set; }
    public double EntryPrice { get; set; }
    public double ExitPrice { get; set; }
    public double SellTaxRate { get; set; }
    public string TaxSource { get; set; }
    public double EstimatedNetProfit { get; set; }
    public double HorizonHours { get; set; }
    /// <summary>
    /// Null until the first BUY order is observed. It is then fixed (D = t0 + HorizonHours, t0 being the creation of that
    /// first order) and never reset by replacement orders.
    /// </summary>
    public DateTime? Deadline { get; set; }
    public string ConfidenceStatus { get; set; }
    /// <summary>Probability (fraction 0..1) of completing the cycle by the deadline. Null means unknown, never 0 or 100.</summary>
    public double? Probability { get; set; }
    /// <summary>Lower confidence bound of <see cref="Probability"/> as a fraction between 0 and 1; null when unknown.</summary>
    public double? LowerBound { get; set; }
    /// <summary>Probability bar the engine used to qualify the order (fraction 0..1); null on plans stored before it was reported.</summary>
    public double? QualifyThreshold { get; set; }
    /// <summary>"simulation" or "prospective"; null when unknown.</summary>
    public string EvidenceSource { get; set; }
    public List<string> ReasonCodes { get; set; }
}

public class GoalStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public StepKind Kind { get; set; }
    public string ItemTag { get; set; }
    public long Quantity { get; set; }
    public List<GoalStep> Children { get; set; } = new();
    public DateTime? DoneAt { get; set; }
    public DateTime? OrderedAt { get; set; }
    /// <summary>Amount of the item the chat confirmed as listed or sold, only used by the Sell step.</summary>
    public long Sold { get; set; }
    public string Note { get; set; }
    /// <summary>Price per unit the plan assumed when the goal was created; null for goals created before this field existed or when unknown.</summary>
    public double? UnitPrice { get; set; }
}

[JsonConverter(typeof(StringEnumConverter), typeof(Newtonsoft.Json.Serialization.CamelCaseNamingStrategy))]
public enum StepKind { BuyOrder, InstaBuy, Npc, Auction, Craft, Forge, Sell }

[JsonConverter(typeof(StringEnumConverter), typeof(Newtonsoft.Json.Serialization.CamelCaseNamingStrategy))]
public enum StepStatus { Todo, Ordered, Ready, Forging, Done }

public class GoalProgress
{
    public Goal Goal { get; set; }
    public Dictionary<string, StepStatus> Status { get; set; } = new();
    public List<GoalStep> Actionable { get; set; } = new();
    public int Done { get; set; }
    public int Total { get; set; }
    public bool Complete { get; set; }
}
