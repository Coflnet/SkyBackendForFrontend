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

        private static FlipperService CreateFastService(InMemoryFlipMatchTracker tracker = null)
        {
            tracker ??= new InMemoryFlipMatchTracker();
            var service = new FlipperService(null, null, tracker);
            service.Handoff = new TierHandoff(tracker)
            {
                Step = TimeSpan.FromMilliseconds(20),
                GraceMin = TimeSpan.FromMilliseconds(150),
                GraceMax = TimeSpan.FromMilliseconds(150)
            };
            service.PreApiLowPriceHandler = (s, a) => Task.CompletedTask;
            return service;
        }

        private static LowPricedAuction CreateCascadeFlip(long uid, bool preApi = false, long profit = 100_000)
        {
            var context = new Dictionary<string, string>();
            if (preApi)
                context["pre-api"] = "true";
            return new LowPricedAuction
            {
                Auction = new SaveAuction
                {
                    UId = uid,
                    Uuid = "uuid" + uid,
                    Context = context,
                    FindTime = DateTime.UtcNow,
                    Start = DateTime.UtcNow,
                    StartingBid = 1000
                },
                Finder = LowPricedAuction.FinderType.SNIPER,
                TargetPrice = 1000 + profit,
                AdditionalProps = new Dictionary<string, string>() { ["mVal"] = (1000 + profit).ToString() }
            };
        }

        private static async Task<MockConnection[]> AddAllTiers(FlipperService service)
        {
            var cons = new[] { new MockConnection(), new MockConnection(), new MockConnection(), new MockConnection() };
            service.AddConnectionPlus(cons[0], false);
            service.AddConnection(cons[1], false);
            service.AddStarterConnection(cons[2], false);
            service.AddNonConnection(cons[3], false);
            await Task.CompletedTask;
            return cons;
        }

        private static async Task WaitCascades(FlipperService service)
        {
            var timeout = Stopwatch.StartNew();
            while (service.CascadesInFlight > 0 && timeout.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(10);
            Assert.That(service.CascadesInFlight, Is.Zero, "cascades did not finish");
        }

        private static Task WaitAll(params MockConnection[] cons)
        {
            return Task.WhenAll(cons.Select(c => c.FirstBatch.Task)).WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Test]
        public async Task UncontestedFlipReachesAllTiersInOrder()
        {
            var service = CreateFastService();
            var cons = await AddAllTiers(service);

            await service.DeliverLowPricedAuction(CreateCascadeFlip(1001));
            await WaitAll(cons);

            Assert.That(cons[0].ReceivedAt, Is.LessThanOrEqualTo(cons[1].ReceivedAt));
            Assert.That(cons[1].ReceivedAt, Is.LessThanOrEqualTo(cons[2].ReceivedAt));
            Assert.That(cons[2].ReceivedAt, Is.LessThanOrEqualTo(cons[3].ReceivedAt));
            await WaitCascades(service);
            foreach (var c in cons)
                service.RemoveConnection(c);
        }

        [Test]
        public async Task SameAuctionDeliveredTwiceIsAuditedOnce()
        {
            var service = CreateFastService();
            var audits = 0;
            service.MatchAudit = new HandoffAudit(new InMemoryFlipMatchTracker(), _ => { Interlocked.Increment(ref audits); return Task.CompletedTask; });

            await service.DeliverLowPricedAuction(CreateCascadeFlip(1020));
            await service.DeliverLowPricedAuction(CreateCascadeFlip(1020, profit: 300_000));
            await WaitCascades(service);
            await WaitFor(() => Volatile.Read(ref audits) >= 1);
            await Task.Delay(100);

            Assert.That(Volatile.Read(ref audits), Is.EqualTo(1));
        }

        [Test]
        public async Task PreApiFlipIsAuditedButPublicBedIsNot()
        {
            var service = CreateFastService();
            var audits = 0;
            service.MatchAudit = new HandoffAudit(new InMemoryFlipMatchTracker(), _ => { Interlocked.Increment(ref audits); return Task.CompletedTask; });
            var flip = CreateCascadeFlip(1021, preApi: true);
            flip.Auction.Start = DateTime.UtcNow - TimeSpan.FromSeconds(30);

            await service.DeliverLowPricedAuction(flip);
            await WaitCascades(service);
            await WaitFor(() => Volatile.Read(ref audits) >= 1);

            Assert.That(Volatile.Read(ref audits), Is.EqualTo(1));
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(10);
        }

        [Test]
        public async Task HigherTierMatchDelaysPremiumByGrace()
        {
            var tracker = new InMemoryFlipMatchTracker();
            var service = CreateFastService(tracker);
            var cons = await AddAllTiers(service);
            var flip = CreateCascadeFlip(1002);
            tracker.ReportMatch(flip.UId, AccountTier.PREMIUM_PLUS);

            await service.DeliverLowPricedAuction(flip);
            await WaitAll(cons[0], cons[1]);

            var gap = cons[1].ReceivedAt - cons[0].ReceivedAt;
            Assert.That(gap, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(120)));
            await WaitCascades(service);
            foreach (var c in cons)
                service.RemoveConnection(c);
        }

        [Test]
        public async Task FreeConnectionReceivesFlips()
        {
            var service = CreateFastService();
            var con = new MockConnection(tier: AccountTier.NONE);
            service.AddNonConnection(con, false);

            await service.DeliverLowPricedAuction(CreateCascadeFlip(1003));

            await con.FirstBatch.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitCascades(service);
            service.RemoveConnection(con);
        }

        [Test]
        public async Task ConnectionMovedToFreeTierKeepsWorkers()
        {
            var service = new FlipperService(null, null);
            var connection = new MockConnection(
                tier: AccountTier.PREMIUM,
                expiresAt: DateTime.UtcNow + TimeSpan.FromMilliseconds(100));
            connection.CurrentTier = AccountTier.NONE;
            service.AddConnection(connection, false);
            await WaitForExclusiveTier(service, connection, "SlowSubs");

            GetSubscriptions(service, "SlowSubs")[connection.Id].AddLowPriced(CreateLowPricedAuction(highProfit: true));

            await connection.FirstBatch.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.RemoveConnection(connection);
        }

        [Test]
        public async Task OnlyHighProfitFlipsGoIntoHistory()
        {
            var service = CreateFastService();
            var con = new MockConnection(tier: AccountTier.NONE);
            service.AddNonConnection(con, false);

            await service.DeliverLowPricedAuction(CreateCascadeFlip(1004, profit: 100_000));
            await service.DeliverLowPricedAuction(CreateCascadeFlip(1005, profit: 300_000));

            await WaitCascades(service);
            Assert.That(service.Flipps.Select(f => f.Uuid), Is.EqualTo(new[] { "uuid1005" }));
            service.RemoveConnection(con);
        }

        [Test]
        public async Task PublicBedReleasesPremiumAndStarterImmediatelyAndHoldsFreeUntilApiUpdate()
        {
            var service = CreateFastService();
            var flip = CreateCascadeFlip(1006, preApi: true);
            var start = flip.Auction.Start;
            service.ApiClock.Set(start - TimeSpan.FromSeconds(59.7)); // api update 300ms after start
            var updateAt = start + TimeSpan.FromMilliseconds(300);
            var cons = await AddAllTiers(service);

            await service.DeliverLowPricedAuction(flip).WaitAsync(TimeSpan.FromSeconds(5));
            await WaitAll(cons);

            Assert.That(cons[1].LastBatchFlip.AdditionalProps["pubbed"], Is.EqualTo("y"));
            Assert.That(cons[2].LastBatchFlip.AdditionalProps["pubbed"], Is.EqualTo("y"));
            Assert.That(cons[1].ReceivedAt, Is.LessThan(updateAt));
            Assert.That(cons[2].ReceivedAt, Is.LessThan(updateAt));
            Assert.That(cons[3].ReceivedAt, Is.GreaterThanOrEqualTo(updateAt + TimeSpan.FromMilliseconds(15)));
            await WaitCascades(service);
            foreach (var c in cons)
                service.RemoveConnection(c);
        }

        [Test]
        public async Task NonPublicBedPreApiFlipWaitsForApiVisibility()
        {
            var service = CreateFastService();
            var flip = CreateCascadeFlip(1007, preApi: true);
            flip.Auction.Start = DateTime.UtcNow - TimeSpan.FromSeconds(20); // visible after ~0.5s
            service.ApiClock.Set(flip.Auction.Start - TimeSpan.FromSeconds(20)); // next update 40s after start
            var cons = await AddAllTiers(service);

            await service.DeliverLowPricedAuction(flip);
            await Task.Delay(200);

            Assert.That(cons[0].BatchCount, Is.EqualTo(1));
            Assert.That(cons[1].BatchCount, Is.Zero);
            await WaitAll(cons);
            await WaitCascades(service);
            foreach (var c in cons)
                service.RemoveConnection(c);
        }

        [Test]
        public async Task SoldDuringCascadeIsMarkedForFreeTier()
        {
            var service = CreateFastService();
            service.Handoff = new TierHandoff(new InMemoryFlipMatchTracker()) { Step = TimeSpan.FromMilliseconds(300) };
            var cons = await AddAllTiers(service);
            var flip = CreateCascadeFlip(1008);

            await service.DeliverLowPricedAuction(flip);
            service.AuctionSold(new SaveAuction { UId = flip.UId, Uuid = flip.Auction.Uuid, End = DateTime.UtcNow });
            await WaitAll(cons);

            Assert.That(cons[3].LastBatchFlip.AdditionalProps["sold"], Is.EqualTo("y"));
            Assert.That(cons[0].LastBatchFlip.AdditionalProps.ContainsKey("sold"), Is.False);
            await WaitCascades(service);
            foreach (var c in cons)
                service.RemoveConnection(c);
        }

        [Test]
        public async Task SlowLowerTiersDoNotDelayLaterFlipsOfTheBatch()
        {
            var service = CreateFastService();
            service.Handoff = new TierHandoff(new InMemoryFlipMatchTracker()) { Step = TimeSpan.FromMilliseconds(300) };
            var cons = await AddAllTiers(service);
            var watch = Stopwatch.StartNew();

            await service.DeliverLowPricedAuctions(new[] { CreateCascadeFlip(1009), CreateCascadeFlip(1010), CreateCascadeFlip(1011) });
            while (cons[0].FlipCount < 3 && watch.ElapsedMilliseconds < 5000)
                await Task.Delay(5);

            Assert.That(cons[0].FlipCount, Is.EqualTo(3));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(250));
            await WaitCascades(service);
            foreach (var c in cons)
                service.RemoveConnection(c);
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
            public DateTime? ReceivedAt;
            public int FlipCount;
            public LowPricedAuction LastBatchFlip;
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
                return Task.FromResult(true);
            }

            public void UpdateSettings(SettingsChange settings)
            {
                throw new System.NotImplementedException();
            }

            public void Log(string message, LogLevel level = LogLevel.Information)
            {
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
                LastBatchFlip = flips.LastOrDefault();
                Interlocked.Add(ref FlipCount, flips.Count());
                ReceivedAt ??= DateTime.UtcNow;
                Interlocked.Increment(ref BatchCount);
                FirstBatch.TrySetResult();
                return Task.CompletedTask;
            }
        }
    }
}
