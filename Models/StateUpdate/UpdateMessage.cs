using System;
using System.Collections.Generic;
using MessagePack;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

#nullable enable
[MessagePackObject]
public class UpdateMessage
{
    [Key(0)]
    public UpdateKind Kind;

    [Key(1)]
    public DateTime ReceivedAt;
    [Key(2)]
    public ChestView? Chest;
    [Key(3)]
    public List<string>? ChatBatch;
    [Key(4)]
    public string? PlayerId;
    [Key(5)]
    public string? UserId { get; set; }
    [Key(6)]
    public StateSettings? Settings { get; set; }
    [Key(7)]
    public string[]? Scoreboard { get; set; }
    [Key(8)]
    public string[]? Tab { get; set; }
    /// <summary>
    /// Id of the achievement to unlock, set when <see cref="Kind"/> is <see cref="UpdateKind.Achievement"/>.
    /// Routed through the update pipeline (instead of an http call) so it reaches the instance that holds
    /// the players live state, which is partitioned across replicas by player id.
    /// </summary>
    [Key(9)]
    public string? AchievementId { get; set; }
    /// <summary>
    /// Task the player manually claimed, set when <see cref="Kind"/> is <see cref="UpdateKind.TaskClaim"/>.
    /// Null clears the claim. Routed through the pipeline like the achievement id so it reaches the
    /// instance holding the players live state.
    /// </summary>
    [Key(10)]
    public string? ClaimedTask { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is UpdateMessage message &&
               Kind == message.Kind &&
               ReceivedAt == message.ReceivedAt &&
               EqualityComparer<ChestView?>.Default.Equals(Chest, message.Chest) &&
               EqualityComparer<List<string>?>.Default.Equals(ChatBatch, message.ChatBatch) &&
               PlayerId == message.PlayerId &&
               UserId == message.UserId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, ReceivedAt, Chest, ChatBatch, PlayerId, UserId);
    }


    public enum UpdateKind
    {
        UNKOWN,
        CHAT,
        INVENTORY,
        API = 4,
        Setting = 8,
        Scoreboard = 16,
        Tab = 32,
        Achievement = 64,
        TaskClaim = 128
    }
}

[MessagePackObject]
public class StateSettings
{
    [Key(0)]
    public bool DisableTradeTracking { get; set; }
    [Key(1)]
    public bool DisableBazaarTracking { get; set; }
    [Key(2)]
    public bool DisableKuudraTracking { get; set; }
}

[MessagePackObject]
public class ChestView
{
    /// <summary>
    /// All items in the ui view
    /// </summary>
    [Key(0)]
    public List<Item> Items = new();
    /// <summary>
    /// The name of the chest
    /// </summary>
    [Key(1)]
    public string Name = string.Empty;
    /// <summary>
    /// The position of the chest (if inventory is, also, a chest)
    /// </summary>
    [Key(2)]
    public BlockPos? Position;
    /// <summary>
    /// The time when the chest was opened (or server receive time)
    /// </summary>
    [Key(3)]
    public DateTime OpenedAt;
}


#nullable restore