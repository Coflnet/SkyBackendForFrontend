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
