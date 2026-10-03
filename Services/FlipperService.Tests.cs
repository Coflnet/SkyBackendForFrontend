using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
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
        public async Task PremiumPlusRefreshMovesConnectionExclusivelyToPremium()
        {
            var service = new FlipperService(null, null);
            var connection = new MockConnection(
                tier: AccountTier.PREMIUM_PLUS,
                expiresAt: DateTime.UtcNow + TimeSpan.FromMilliseconds(100));
            connection.CurrentTier = AccountTier.PREMIUM;
            service.AddConnectionPlus(connection, false);

            await WaitForExclusiveTier(service, connection, "Subs");

            Assert.That(connection.TierRefreshCount, Is.EqualTo(1));
            service.RemoveConnection(connection);
        }

        [TestCase(AccountTier.STARTER_PREMIUM, "StarterSubs")]
        [TestCase(AccountTier.NONE, "SlowSubs")]
        public async Task PremiumRefreshMovesConnectionExclusivelyToLowerTier(AccountTier currentTier, string targetSubscriptions)
        {
            var service = new FlipperService(null, null);
            var connection = new MockConnection(
                tier: AccountTier.PREMIUM,
                expiresAt: DateTime.UtcNow + TimeSpan.FromMilliseconds(100));
            connection.CurrentTier = currentTier;
            service.AddConnection(connection, false);

            await WaitForExclusiveTier(service, connection, targetSubscriptions);

            Assert.That(connection.TierRefreshCount, Is.EqualTo(1));
            service.RemoveConnection(connection);
        }

        [Test]
        public async Task ExpiredPremiumStopsReceivingPremiumFlipsWhileFlipsKeepArriving()
        {
            var service = new FlipperService(null, null);
            var connection = new MockConnection(
                tier: AccountTier.PREMIUM,
                expiresAt: DateTime.UtcNow + TimeSpan.FromMilliseconds(100));
            connection.CurrentTier = AccountTier.NONE;
            service.AddConnection(connection, false);

            using var keepSending = new CancellationTokenSource();
            var sender = Task.Run(async () =>
            {
                while (!keepSending.IsCancellationRequested)
                {
                    RoutePremiumFlip(service, connection, CreateLowPricedAuction(highProfit: true));
                    await Task.Delay(5);
                }
            });

            try
            {
                await WaitForExclusiveTier(service, connection, "SlowSubs");
            }
            finally
            {
                keepSending.Cancel();
                await sender;
            }

            var batchesAfterRefresh = connection.BatchCount;
            for (var i = 0; i < 10; i++)
                RoutePremiumFlip(service, connection, CreateLowPricedAuction(highProfit: true));
            await Task.Delay(100);

            Assert.That(connection.TierRefreshCount, Is.EqualTo(1));
            Assert.That(connection.BatchCount, Is.EqualTo(batchesAfterRefresh));
            service.RemoveConnection(connection);
        }

        [Test]
        public async Task UnchangedExpiryIsRetainedAfterFirstPeriodicRefresh()
        {
            var service = new FlipperService(null, null);
            var connection = new MockConnection(
                tier: AccountTier.PREMIUM,
                expiresAt: DateTime.UtcNow + TimeSpan.FromSeconds(35))
            {
                CurrentTier = AccountTier.NONE,
                ChangeTierOnlyAfterExpiry = true
            };
            service.AddConnection(connection, false);

            await WaitForExclusiveTier(service, connection, "SlowSubs", TimeSpan.FromSeconds(40));

            Assert.That(connection.TierRefreshCount, Is.EqualTo(2));
            service.RemoveConnection(connection);
        }

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

        private static async Task WaitForExclusiveTier(
            FlipperService service,
            IFlipConnection connection,
            string targetSubscriptions,
            TimeSpan? waitTimeout = null)
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < (waitTimeout ?? TimeSpan.FromSeconds(2)))
            {
                var ownedTiers = new[] { "Subs", "SuperSubs", "StarterSubs", "SlowSubs" }
                    .Where(name => OwnsSubscription(service, connection, name))
                    .ToList();
                if (ownedTiers.SequenceEqual(new[] { targetSubscriptions }))
                    return;
                await Task.Delay(10);
            }
            Assert.Fail($"Connection did not move exclusively to {targetSubscriptions}");
        }

        private static bool OwnsSubscription(FlipperService service, IFlipConnection connection, string fieldName)
        {
            var subscriptions = GetSubscriptions(service, fieldName);
            return subscriptions.TryGetValue(connection.Id, out var wrapper)
                && ReferenceEquals(wrapper.Connection, connection);
        }

        private static void RoutePremiumFlip(FlipperService service, IFlipConnection connection, LowPricedAuction flip)
        {
            if (GetSubscriptions(service, "Subs").TryGetValue(connection.Id, out var wrapper))
                wrapper.AddLowPriced(flip);
        }

        private static ConcurrentDictionary<long, FlipConWrapper> GetSubscriptions(FlipperService service, string fieldName)
        {
            var field = typeof(FlipperService).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return (ConcurrentDictionary<long, FlipConWrapper>)field.GetValue(service);
        }

        private static LowPricedAuction CreateLowPricedAuction(bool highProfit = false)
        {
            return new LowPricedAuction
            {
                Auction = new SaveAuction
                {
                    Context = new Dictionary<string, string>(),
                    FindTime = DateTime.UtcNow,
                    StartingBid = 0
                },
                Finder = LowPricedAuction.FinderType.Rust,
                TargetPrice = highProfit ? 500_000 : 0,
                AdditionalProps = new Dictionary<string, string>()
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
            public MockConnection(
                long? id = null,
                AccountTier tier = AccountTier.PREMIUM,
                DateTime? expiresAt = null)
            {
                Id = id ?? Random.Shared.NextInt64();
                CurrentTier = tier;
                AccountInfo = new AccountInfo
                {
                    Tier = tier,
                    ExpiresAt = expiresAt ?? DateTime.UtcNow + TimeSpan.FromHours(2)
                };
            }

            public FlipSettings Settings => new FlipSettings();

            public long Id { get; }

            public string UserId => "1";

            public string GameServer => "skyblock";

            public AccountTier CurrentTier { get; set; }

            public bool ChangeTierOnlyAfterExpiry { get; set; }

            public AccountInfo AccountInfo { get; }

            public int TierRefreshCount;

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

            public Task<AccountTier> UserAccountTier()
            {
                Interlocked.Increment(ref TierRefreshCount);
                if (ChangeTierOnlyAfterExpiry && DateTime.UtcNow < AccountInfo.ExpiresAt)
                    return Task.FromResult(AccountInfo.Tier);
                return Task.FromResult(CurrentTier);
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
