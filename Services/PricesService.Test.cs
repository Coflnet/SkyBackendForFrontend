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
    }
}