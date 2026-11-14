using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared.Test
{
    public class PricesServiceTests
    {
        /// <summary>
        /// Sum multiple sell orders to get a price for multiple items of same type
        /// </summary>
        [Test]
        public void MultiSellOrderSpan()
        {
            var orders = new List<Order>(){
                new (){Amount = 3,PricePerUnit = 4},
                new (){Amount = 4,PricePerUnit = 1},
                new (){Amount = 5,PricePerUnit = 100},
            };
            var count = 8;
            double totalCost = new PricesService(null, null, null, null).GetBazaarCostForCount(orders, count);
            Assert.That(116,Is.EqualTo(totalCost));
        }
        [Test]
        public void MultiBuyOrderSpan()
        {
            var orders = new List<Order>(){
                new (){Amount = 3,PricePerUnit = 5},
                new (){Amount = 4,PricePerUnit = 4},
                new (){Amount = 5,PricePerUnit = 3},
            };
            var count = 8;
            double totalCost = new PricesService(null, null, null, null).GetBazaarCostForCount(orders, count);
            Assert.That(34,Is.EqualTo(totalCost));
        }

        [Test]
        public void SingleOrder()
        {
            var orders = new List<Order>(){
                new (){Amount = 3,PricePerUnit = 4},
                new (){Amount = 4,PricePerUnit = 1}
            };
            var count = 3;
            double totalCost = new PricesService(null, null, null, null).GetBazaarCostForCount(orders, count);
            Assert.That(12,Is.EqualTo(totalCost));
        }

        private static List<SaveAuction> Listings(DateTime now) => new()
        {
            new() { Uuid = "fresh", Start = now.AddSeconds(-30), End = now.AddHours(1), Bin = true, StartingBid = 100 },
            new() { Uuid = "old", Start = now.AddMinutes(-3), End = now.AddHours(1), Bin = true, StartingBid = 200 },
            new() { Uuid = "ended", Start = now.AddHours(-3), End = now.AddMinutes(-1), Bin = true, StartingBid = 50 },
            new() { Uuid = "bid", Start = now.AddHours(-1), End = now.AddHours(1), Bin = true, StartingBid = 60, HighestBidAmount = 70 },
            new() { Uuid = "auction", Start = now.AddHours(-1), End = now.AddHours(1), Bin = false, StartingBid = 40 },
        };

        [Test]
        public void ActiveBinFilterCountsFreshListingsWithoutMinAge()
        {
            var now = DateTime.Now;
            Assert.That(Listings(now).AsQueryable().WhereActiveBin(now).Select(a => a.Uuid), Is.EquivalentTo(new[] { "fresh", "old" }));
        }

        [Test]
        public void ActiveBinFilterIgnoresListingsYoungerThanMinAge()
        {
            var now = DateTime.Now;
            Assert.That(Listings(now).AsQueryable().WhereActiveBin(now, TimeSpan.FromMinutes(2)).Select(a => a.Uuid), Is.EqualTo(new[] { "old" }));
        }

        [Test]
        public void ActiveBinFilterTreatsZeroMinAgeAsUnset()
        {
            var now = DateTime.Now;
            Assert.That(Listings(now).AsQueryable().WhereActiveBin(now, TimeSpan.Zero).Count(), Is.EqualTo(2));
        }
    }
}
