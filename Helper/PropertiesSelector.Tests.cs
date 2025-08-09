using System;
using System.Linq;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Helper
{
    public class PropertiesSelectorTests
    {
        [Test]
        public void DragonHunter()
        {
            var auction = new SaveAuction()
            {
                Enchantments = new System.Collections.Generic.List<Enchantment>() {
                    new Enchantment(Enchantment.EnchantmentType.dragon_hunter, 6)
                    }
            };
            var prop = PropertiesSelector.GetProperties(auction).Select(p => p.Value).First();
            Assert.That("Gravity: 6",Is.EqualTo(prop));
        }
        [Test]
        public void BedTime()
        {
            var auction = new SaveAuction()
            {
                Enchantments = new System.Collections.Generic.List<Enchantment>(),
                Start = DateTime.UtcNow.AddSeconds(-10)
            };
            var prop = PropertiesSelector.GetProperties(auction).Select(p => p.Value).First();
            Assert.That("Bed: 9s",Is.EqualTo(prop));
        }
        [TestCase("0:0:255", "§10000FF")]
        [TestCase("0:190:0", "§200BE00")]
        [TestCase("10:0:17", "§00A0011")]
        [TestCase("84:20:110", "§554146E")]
        [TestCase("31:0:48", "§51F0030")]
        public void FormatHex(string input, string shwon)
        {
            var auction = new SaveAuction()
            {
                FlatenedNBT = new System.Collections.Generic.Dictionary<string, string>()
                { { "color", input } }
            };
            var prop = PropertiesSelector.GetProperties(auction).Select(p => p.Value).First();
            Assert.That($"Color: {shwon}§f",Is.EqualTo(prop));
        }
    }
}