using System;
using System.Security.Cryptography;
using System.Text;
using Coflnet.Kafka;
using Coflnet.Sky.Core;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Coflnet.Sky.Commands.Shared
{
    public interface IStateUpdateService
    {
        void Produce(string playerId, UpdateMessage message);
    }

    public class StateUpdateService : IStateUpdateService
    {
        IConfiguration config;
        ILogger<StateUpdateService> logger;
        IProducer<string, UpdateMessage> producer;

        public StateUpdateService(IConfiguration config, ILogger<StateUpdateService> logger, Sky.Kafka.KafkaCreator kafkaCreator)
        {
            this.logger = logger;
            this.config = config;
            producer = kafkaCreator.BuildProducer<string, UpdateMessage>(true, b => b.SetDefaultPartitioner((topic, pcount, key, isNull) =>
            {
                if (isNull || key.Length < 3)
                    return Random.Shared.Next() % pcount;
                // there 1 byte prefixed to the key, so we skip it to figure out the playerId
                var name = key.ToArray().Skip(1).TakeWhile(c => c != '-').Select(c => (byte)c).ToArray();
                byte[] encoded = SHA256.HashData(name);
                int partition = BitConverter.ToUInt16(encoded, 0) % pcount;
                return partition;
            }));

            _ = kafkaCreator.CreateTopicIfNotExist(config["TOPICS:STATE_UPDATE"], 16);
        }

        public void Produce(string playerId, UpdateMessage message)
        {
            message.PlayerId = playerId;
            producer.Produce(config["TOPICS:STATE_UPDATE"], new()
            {
                Key = string.IsNullOrEmpty(playerId) ? null : $"{playerId}-{message.GetHashCode()}",
                Value = message,
                Timestamp = new(DateTime.UtcNow)
            });
        }
    }
}
