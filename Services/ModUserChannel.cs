using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Shared contract for pushing messages to a user's connected mod sessions over the events Redis
/// (<c>EVENTS_REDIS_HOST</c>, the keyed "bazaar" multiplexer in SkyApi and SkyModCommands).
/// SkyApi publishes, every SkyModCommands session of that user subscribes while it is logged in.
/// </summary>
public static class ModUserChannel
{
    public const string ExecuteKind = "execute";
    public const string DisplayKind = "display";
    public const string ExpectKind = "expect";

    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public class UserMessage
    {
        /// <summary>One of <see cref="ExecuteKind"/>, <see cref="DisplayKind"/>, <see cref="ExpectKind"/>.</summary>
        public string Kind { get; set; }
        /// <summary>Chat command to run (execute) or the command the user is expected to type next (expect).</summary>
        public string Command { get; set; }
        /// <summary>InfoDisplay payload (camelCase, matches the mod's infoDisplay message) for <see cref="DisplayKind"/>.</summary>
        public JObject Display { get; set; }
        /// <summary>When set only sessions of this Minecraft account act on the message.</summary>
        public string MinecraftUuid { get; set; }
        /// <summary>Seconds the message stays relevant (expect).</summary>
        public int? Ttl { get; set; }
    }

    public static string Channel(string userId) => $"mod:user:{userId}";

    public static string Serialize(UserMessage message) => JsonConvert.SerializeObject(message, Settings);

    public static UserMessage Deserialize(string json) => JsonConvert.DeserializeObject<UserMessage>(json, Settings);

    /// <summary>
    /// Publishes to all live sessions of <paramref name="userId"/>.
    /// Returns the number of subscribed multiplexers (SkyModCommands pods holding a session for the user); 0 means nobody received it.
    /// </summary>
    public static Task<long> PublishAsync(IConnectionMultiplexer redis, string userId, UserMessage message)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId is required", nameof(userId));
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        return redis.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel(userId)), Serialize(message));
    }

    /// <summary>
    /// Subscribes to messages for <paramref name="userId"/>. The handler returns false to stop the subscription.
    /// Dispose the returned queue (Unsubscribe) when the session ends.
    /// </summary>
    public static async Task<ChannelMessageQueue> SubscribeAsync(IConnectionMultiplexer redis, string userId, Func<UserMessage, bool> onMessage)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId is required", nameof(userId));
        var queue = await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(Channel(userId)));
        queue.OnMessage(msg =>
        {
            UserMessage parsed;
            try
            {
                parsed = Deserialize(msg.Message);
            }
            catch (JsonException)
            {
                return;
            }
            if (parsed == null)
                return;
            if (!onMessage(parsed))
                queue.Unsubscribe();
        });
        return queue;
    }
}
