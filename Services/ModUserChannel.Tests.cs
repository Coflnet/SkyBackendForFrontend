using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using StackExchange.Redis;

namespace Coflnet.Sky.Commands.Shared;

public class ModUserChannelTests
{
    [Test]
    public void ChannelIsScopedToUser()
    {
        ModUserChannel.Channel("abc").Should().Be("mod:user:abc");
    }

    [Test]
    public void SerializesCamelCaseAndRoundTrips()
    {
        var message = new ModUserChannel.UserMessage
        {
            Kind = ModUserChannel.DisplayKind,
            Display = JObject.FromObject(new { id = 3, title = "Goal", lines = new object[] { "a" } }),
            Ttl = 600
        };
        var json = ModUserChannel.Serialize(message);
        json.Should().Contain("\"kind\":\"display\"").And.Contain("\"ttl\":600").And.NotContain("command");
        var back = ModUserChannel.Deserialize(json);
        back.Kind.Should().Be("display");
        back.Display["id"].Value<int>().Should().Be(3);
        back.Ttl.Should().Be(600);
    }

    [Test]
    public async Task PublishUsesUserChannelAndReturnsReceivers()
    {
        var subscriber = new Mock<ISubscriber>();
        subscriber.Setup(s => s.PublishAsync(RedisChannel.Literal("mod:user:u1"), It.Is<RedisValue>(v => v.ToString().Contains("\"command\":\"/bz Enchanted Diamond\"")), CommandFlags.None))
            .ReturnsAsync(2);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetSubscriber(null)).Returns(subscriber.Object);

        var receivers = await ModUserChannel.PublishAsync(redis.Object, "u1", new ModUserChannel.UserMessage { Kind = "execute", Command = "/bz Enchanted Diamond" });

        receivers.Should().Be(2);
        subscriber.VerifyAll();
    }

    [Test]
    public void PublishRejectsMissingUser()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        var act = () => ModUserChannel.PublishAsync(redis.Object, " ", new ModUserChannel.UserMessage { Kind = "execute" });
        act.Should().ThrowAsync<System.ArgumentException>();
    }
}
