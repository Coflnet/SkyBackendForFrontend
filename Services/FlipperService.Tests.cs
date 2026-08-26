using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Coflnet.Sky.Commands.Tests
{
    public static class TestConstants
    {
        public static int DelayMultiplier = 20;
    }
    public class FlipperServiceTests
    {
        [Test]
        public async Task RemovingReplacedConnectionDoesNotUnsubscribeCurrentOwner()
        {
            var service = new FlipperService(null, null);
            var oldConnection = new MockConnection(42);
            var replacement = new MockConnection(42);
            service.AddConnection(oldConnection, false);
            service.AddConnection(replacement, false);

            service.RemoveConnection(oldConnection);
            var current = service.Connections.Single();
            Assert.That(current.Connection, Is.SameAs(replacement));
            Assert.That(current.AddLowPriced(CreateLowPricedAuction()), Is.True);

            await replacement.FirstBatch.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(oldConnection.BatchCount, Is.Zero);
            Assert.That(replacement.BatchCount, Is.EqualTo(1));
            Assert.That(service.PremiumUserCount, Is.EqualTo(1));

            service.RemoveConnection(replacement);

            Assert.That(service.Connections, Is.Empty);
            Assert.That(current.Closed, Is.True);
            Assert.That(service.PremiumUserCount, Is.Zero);
        }

        private static LowPricedAuction CreateLowPricedAuction()
        {
            return new LowPricedAuction
            {
                Auction = new SaveAuction
                {
                    Context = new Dictionary<string, string>(),
                    FindTime = DateTime.UtcNow
                },
                Finder = LowPricedAuction.FinderType.Rust
            };
        }

        // test disabled because it fails in kaniko [Test]
        public async Task ReceiveAndDistribute()
        {
            var service = new FlipperService(null, null);
            var con = new MockConnection();
            service.AddConnection(con);
            //for (int i = 0; i < 1; i++)
            //    service.AddConnection(new MockConnection());
            var auction = new SaveAuction() { NbtData = new NbtData(), Enchantments = new System.Collections.Generic.List<Enchantment>() };
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                await service.DeliverLowPricedAuction(new LowPricedAuction()
                {
                    Auction = auction,
                    DailyVolume = 2,
                    Finder = LowPricedAuction.FinderType.AI,
                    TargetPrice = 5
                }).ConfigureAwait(false);
            }
            await Task.Delay(20 * TestConstants.DelayMultiplier).ConfigureAwait(false); // wait for the async sending to finish
            Assert.That(con.LastFlip, Is.Not.Null, "No flip was sent but should have been after " + watch.ElapsedMilliseconds + "ms");
            Assert.That(5,Is.EqualTo(con.LastFlip.MedianPrice));
            Assert.That(2,Is.EqualTo(con.LastFlip.Volume));
            Assert.That(auction,Is.EqualTo(con.LastFlip.Auction));
        }

        public class MockConnection : IFlipConnection
        {
            public MockConnection(long? id = null)
            {
                Id = id ?? Random.Shared.NextInt64();
            }

            public FlipSettings Settings => new FlipSettings();

            public long Id { get; }

            public string UserId => "1";

            public string GameServer => "skyblock";

            public SettingsChange LatestSettings => new SettingsChange()
            {
                Tier = AccountTier.PREMIUM,
                ExpiresAt = DateTime.UtcNow + TimeSpan.FromHours(2)
            };

            public AccountInfo AccountInfo => null;

            public FlipInstance LastFlip;
            public int BatchCount;
            public TaskCompletionSource FirstBatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<bool> SendFlip(FlipInstance flip)
            {
                LastFlip = flip;
                return Task.FromResult(true);
            }

            public Task<bool> SendFlip(LowPricedAuction flip)
            {
                return this.SendFlip(FlipperService.LowPriceToFlip(flip));
            }

            public Task<bool> SendSold(string uuid)
            {
                throw new System.NotImplementedException();
            }

            public void UpdateSettings(SettingsChange settings)
            {
                throw new System.NotImplementedException();
            }

            public void Log(string message, LogLevel level = LogLevel.Information)
            {
                throw new NotImplementedException();
            }

            public Task<bool> SendBatch(IEnumerable<LowPricedAuction> flips)
            {
                return Task.FromResult(true);
            }

            Task IFlipConnection.SendBatch(IEnumerable<LowPricedAuction> flips)
            {
                Interlocked.Increment(ref BatchCount);
                FirstBatch.TrySetResult();
                return Task.CompletedTask;
            }
        }
    }
}
