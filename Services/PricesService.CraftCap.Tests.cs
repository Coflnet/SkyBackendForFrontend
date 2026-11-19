using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared.Test
{
    /// <summary>
    /// Regression: one coin-transfer "auction" (6 Paper sold for 171,753,106 coins) was the only sale in the
    /// 2 day window and set the Paper price to 28.6M. Thin windows are widened and prices capped by craft cost.
    /// </summary>
    public class PricesServiceCraftCapTests
    {
        private class FakePricesService : PricesService
        {
            public List<(double daysAgo, long perUnit)> Sales = new();
            public List<SaveAuction> Bins = new();
            public List<int> QueriedDays = new();

            public FakePricesService(ICraftCostProvider provider)
                : base(null, null, Items(), null, provider) { }

            private static IItemsApi Items()
            {
                var api = new Mock<IItemsApi>();
                api.Setup(a => a.ItemsBazaarTagsGetAsync(It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(new List<string>());
                return api.Object;
            }

            protected override int ResolveItemId(string itemTag, bool forceget = true) => 42;

            protected override Task<List<long>> LoadAuctionSalePrices(int itemId, Dictionary<string, string> filter, int days)
            {
                QueriedDays.Add(days);
                return Task.FromResult(Sales.Where(s => s.daysAgo < days).Select(s => s.perUnit).OrderByDescending(p => p).ToList());
            }

            protected override Task<List<SaveAuction>> LoadLowestBins(int itemId, int count, TimeSpan? minListedFor)
                => Task.FromResult(Bins);
        }

        private class FixedCost : ICraftCostProvider
        {
            private readonly double? cost;
            public FixedCost(double? cost) { this.cost = cost; }
            public Task<double?> GetUnitCraftCost(string itemTag) => Task.FromResult(cost);
        }

        private class ThrowingCost : ICraftCostProvider
        {
            public Task<double?> GetUnitCraftCost(string itemTag) => throw new TimeoutException();
        }

        private static FakePricesService PaperService(ICraftCostProvider provider)
        {
            var service = new FakePricesService(provider);
            // the coin transfer: 171,753,106 / 6 per unit, yesterday
            service.Sales.Add((1, 171_753_106 / 6));
            for (var i = 0; i < 10; i++)
                service.Sales.Add((7, 128));
            return service;
        }

        [Test]
        public async Task ThinWindowFallsBackTo14DaysAndIgnoresOutlier()
        {
            var service = PaperService(null);
            var sumary = await service.GetSumary("PAPER", null);
            Assert.That(service.QueriedDays, Is.EqualTo(new[] { 2, 14 }));
            Assert.That(sumary.Med, Is.EqualTo(128));
            Assert.That(sumary.Volume, Is.EqualTo(11d / 14).Within(0.0001));
        }

        [Test]
        public async Task EnoughSalesKeepTwoDayWindow()
        {
            var service = new FakePricesService(null);
            service.Sales.AddRange(new[] { (0.5, 100L), (1, 110L), (1.5, 120L), (7, 5L) });
            var sumary = await service.GetSumary("X", null);
            Assert.That(service.QueriedDays, Is.EqualTo(new[] { 2 }));
            Assert.That(sumary.Min, Is.EqualTo(100));
            Assert.That(sumary.Volume, Is.EqualTo(1.5).Within(0.0001));
        }

        [Test]
        public async Task SingleOutlierSaleIsCappedByCraftCost()
        {
            var service = new FakePricesService(new FixedCost(5.76));
            service.Sales.Add((1, 171_753_106 / 6));
            var sumary = await service.GetSumary("PAPER", null);
            Assert.That(sumary.Med, Is.LessThanOrEqualTo(57));
            Assert.That(sumary.Max, Is.LessThanOrEqualTo(57));
            Assert.That(sumary.Min, Is.LessThanOrEqualTo(57));
            Assert.That(sumary.Mode, Is.LessThanOrEqualTo(57));
            Assert.That(sumary.Mean, Is.LessThanOrEqualTo(57.6));
        }

        [Test]
        public async Task CurrentPriceWithoutBinIsCappedByCraftCost()
        {
            var service = new FakePricesService(new FixedCost(5.76));
            service.Sales.Add((1, 171_753_106 / 6));
            var price = await service.GetCurrentPrice("PAPER");
            Assert.That(price.Buy, Is.LessThanOrEqualTo(57.6));
            Assert.That(price.Sell, Is.LessThanOrEqualTo(57.6));
            Assert.That(price.IsAh, Is.True);
        }

        [Test]
        public async Task CurrentPriceBinIsCappedForCount()
        {
            var service = new FakePricesService(new FixedCost(5.76));
            service.Bins.Add(new SaveAuction { StartingBid = 1_000_000, Count = 1, Bin = true });
            var price = await service.GetCurrentPrice("PAPER", 1);
            Assert.That(price.Buy, Is.EqualTo(57.6).Within(0.0001));
            Assert.That(price.Sell, Is.EqualTo(57.6).Within(0.0001));
            service.Bins.Clear();
            service.Bins.AddRange(Enumerable.Range(0, 2).Select(_ => new SaveAuction { StartingBid = 1_000_000, Count = 1, Bin = true }));
            price = await service.GetCurrentPrice("PAPER", 2);
            Assert.That(price.Buy, Is.EqualTo(115.2).Within(0.0001));
        }

        [Test]
        public async Task NoCraftCostMeansNoCap()
        {
            var service = new FakePricesService(new FixedCost(null));
            service.Sales.Add((1, 28_625_517));
            var price = await service.GetCurrentPrice("PAPER");
            Assert.That(price.Buy, Is.EqualTo(28_625_517));
        }

        [Test]
        public async Task FailingCraftCostProviderMeansNoCapAndNoException()
        {
            var service = new FakePricesService(new ThrowingCost());
            service.Sales.Add((1, 28_625_517));
            var price = await service.GetCurrentPrice("PAPER");
            Assert.That(price.Buy, Is.EqualTo(28_625_517));
        }
    }
}
