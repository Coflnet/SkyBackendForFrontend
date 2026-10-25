using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class ActiveAuctionReplicaTests
{
    private ActiveAuctionSet set;
    private Mock<IActiveIndexerClient> indexer;
    private FakeLoader loader;
    private ActiveAuctionReplica replica;

    private class FakeLoader : ActiveAuctionDbLoader
    {
        public List<SaveAuction> Data = new();
        public int Calls;
        protected override Task<int> MaxIdAsync(CancellationToken ct) => Task.FromResult(10);
        protected override Task<(int Id, DateTime Start)?> ProbeAsync(int fromId, CancellationToken ct)
            => Task.FromResult<(int, DateTime)?>((fromId, DateTime.UtcNow));
        protected override async IAsyncEnumerable<SaveAuction> QueryChunkAsync(int from, int to, DateTime now, [EnumeratorCancellation] CancellationToken ct)
        {
            Calls++;
            foreach (var a in Data)
                yield return a;
            await Task.CompletedTask;
        }
    }

    private static SaveAuction Auction(long uid, int bids = 0) => new()
    {
        UId = uid,
        Uuid = uid.ToString("x32"),
        Tag = "T",
        End = DateTime.UtcNow.AddHours(1),
        StartingBid = 10,
        Bids = Enumerable.Range(0, bids).Select(_ => new SaveBids()).ToList()
    };

    private static async IAsyncEnumerable<SaveAuction> Stream(IEnumerable<SaveAuction> items)
    {
        foreach (var a in items)
            yield return a;
        await Task.CompletedTask;
    }

    [SetUp]
    public void Setup()
    {
        set = new ActiveAuctionSet();
        indexer = new Mock<IActiveIndexerClient>();
        loader = new FakeLoader();
        var config = new ConfigurationBuilder().Build();
        replica = new ActiveAuctionReplica(config, set, indexer.Object, loader, NullLogger<ActiveAuctionReplica>.Instance)
        {
            IndexerWaitTimeout = TimeSpan.FromMilliseconds(100),
            IndexerRetryDelay = TimeSpan.FromMilliseconds(20)
        };
    }

    [Test]
    public async Task LoadsFromIndexer()
    {
        indexer.Setup(i => i.StreamAllAsync(It.IsAny<CancellationToken>())).Returns(Stream(new[] { Auction(1), Auction(2) }));
        await replica.LoadInitialAsync(default);
        set.Count.Should().Be(2);
        loader.Calls.Should().Be(0);
    }

    [Test]
    public async Task ExecuteMarksReadyAndHonoursKillSwitch()
    {
        indexer.Setup(i => i.StreamAllAsync(It.IsAny<CancellationToken>())).Returns(Stream(new[] { Auction(1) }));
        using var cts = new CancellationTokenSource();
        await replica.StartAsync(cts.Token);
        for (var i = 0; i < 50 && !replica.IsReady; i++)
            await Task.Delay(50);
        replica.IsReady.Should().BeTrue();
        await replica.StopAsync(default);

        var off = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["ACTIVE_STORE:ENABLED"] = "false" }).Build();
        var disabled = new ActiveAuctionReplica(off, new ActiveAuctionSet(), indexer.Object, loader, NullLogger<ActiveAuctionReplica>.Instance);
        await disabled.StartAsync(default);
        await Task.Delay(100);
        disabled.IsReady.Should().BeFalse();
        await disabled.StopAsync(default);
    }

    [Test]
    public async Task FallsBackToDatabaseWhenIndexerStaysUnavailable()
    {
        indexer.Setup(i => i.StreamAllAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ThrowAsync());
        loader.Data.Add(Auction(7));
        await replica.LoadInitialAsync(default);
        set.TryGet(7, out _).Should().BeTrue();
        loader.Calls.Should().BeGreaterThan(0);
    }

    private static async IAsyncEnumerable<SaveAuction> ThrowAsync()
    {
        await Task.Yield();
        throw new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    [Test]
    public async Task ResyncRemovesOnlyAfterTwoMissesAndFetchesMissing()
    {
        set.Upsert(Auction(1), "t");
        set.Upsert(Auction(2), "t");
        var remote = new Dictionary<long, long> { [1] = DateTime.UtcNow.AddHours(1).Ticks, [3] = DateTime.UtcNow.AddHours(1).Ticks };
        indexer.Setup(i => i.GetUidEndTicksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(remote);
        indexer.Setup(i => i.GetAuctionAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(Auction(3));

        await replica.ResyncOnceAsync(default);
        set.TryGet(2, out _).Should().BeTrue("one miss is not enough");
        set.TryGet(3, out _).Should().BeTrue("missing uids are fetched");

        await replica.ResyncOnceAsync(default);
        set.TryGet(2, out _).Should().BeFalse();
        set.TryGet(1, out _).Should().BeTrue();
        indexer.Verify(i => i.GetAuctionAsync(3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ResyncMissCounterResetsWhenAuctionReturns()
    {
        set.Upsert(Auction(2), "t");
        var far = DateTime.UtcNow.AddHours(1).Ticks;
        var withoutTwo = new Dictionary<long, long> { [1] = far };
        var withTwo = new Dictionary<long, long> { [1] = far, [2] = far };
        indexer.SetupSequence(i => i.GetUidEndTicksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(withoutTwo).ReturnsAsync(withTwo).ReturnsAsync(withoutTwo);
        indexer.Setup(i => i.GetAuctionAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Auction(1));
        await replica.ResyncOnceAsync(default);
        await replica.ResyncOnceAsync(default);
        await replica.ResyncOnceAsync(default);
        set.TryGet(2, out _).Should().BeTrue();
    }

    [TestCase(null)]
    [TestCase("empty")]
    public async Task ResyncSkippedWhenIndexerNotReadyOrEmpty(string mode)
    {
        set.Upsert(Auction(1), "t");
        indexer.Setup(i => i.GetUidEndTicksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mode == null ? null : new Dictionary<long, long>());
        await replica.ResyncOnceAsync(default);
        await replica.ResyncOnceAsync(default);
        await replica.ResyncOnceAsync(default);
        set.Count.Should().Be(1);
    }

    [Test]
    public void KafkaHandlersUpsertAndRemove()
    {
        replica.HandleNewAuction(Auction(5), "NEW_AUCTION");
        set.TryGet(5, out _).Should().BeTrue();
        replica.HandleNewAuction(Auction(5, bids: 1), "NEW_BID");
        set.TryGet(5, out var a).Should().BeTrue();
        a.Bids.Should().HaveCount(1);
        replica.HandleRemoved("SOLD_AUCTION", Auction(5));
        set.TryGet(5, out _).Should().BeFalse();
        replica.HandleNewAuction(Auction(5), "NEW_AUCTION"); // tombstoned
        set.TryGet(5, out _).Should().BeFalse();
    }

    [Test]
    public void KafkaMessageWithoutUidGetsOneFromUuid()
    {
        var a = Auction(0);
        a.Uuid = "0123456789abcdef0123456789abcdef";
        replica.HandleNewAuction(a, "NEW_AUCTION");
        set.Count.Should().Be(1);
        replica.HandleRemoved("AUCTION_ENDED", new SaveAuction { Uuid = a.Uuid });
        set.Count.Should().Be(0);
    }
}
