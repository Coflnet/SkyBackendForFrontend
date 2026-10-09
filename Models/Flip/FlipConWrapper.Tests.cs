using System.Reflection;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared
{
    public class FlipConWrapperTests
    {
        [Test]
        public void TierRefreshIsSplitIntoNamedMethods()
        {
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

            Assert.That(typeof(FlipConWrapper).GetMethod("RunTierRefresh", privateInstance), Is.Not.Null);
            Assert.That(typeof(FlipConWrapper).GetMethod("RefreshTier", privateInstance), Is.Not.Null);
        }
    }
}
