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
        Scoreboard = 16
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
    public string Name;
    /// <summary>
    /// The position of the chest (if inventory is, also, a chest)
    /// </summary>
    [Key(2)]
    public BlockPos Position;
}


#nullable restore