using System;
using System.Threading.Tasks;
using Coflnet.Sky.EventBroker.Client.Api;
using Coflnet.Sky.EventBroker.Client.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.Shared
{
    public class EventBrokerClient
    {
        private ConnectionMultiplexer con;
        private MessageApi api;
        public EventBrokerClient(IConfiguration config, ILogger<EventBrokerClient> logger)
        {
            if(config["EVENTS_REDIS_HOST"] == null)
            {
                logger.LogError("EVENTS_REDIS_HOST is not set");
            }
            con = ConnectionMultiplexer.Connect(config["EVENTS_REDIS_HOST"]);
            api = new MessageApi(config["EVENTS_BASE_URL"]);
        }

        public ChannelMessageQueue SubEvents(string userId, Action<MessageContainer> onEvent)
        {
            var sub = con.GetSubscriber().Subscribe(RedisChannel.Literal("uev" + userId));
            sub.OnMessage((msg) =>
            {
                var deserialized = JsonConvert.DeserializeObject<MessageContainer>(msg.Message);
                onEvent.Invoke(deserialized);
                Task.Run(async () => await api.MessageConfirmAuctionIdPostAsync(deserialized.Reference).ConfigureAwait(false));
            });
            return sub;
        }
    }
}
