using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands;

public class BazaarUtilsTests
{
    [Test]
    public void EnchantmentBookTagIsConvertedToFullEnchantmentName()
    {
        var result = BazaarUtils.GetSearchValue("ENCHANTMENT_TURBO_PUMPKIN_5", "Enchanted Book");

        Assert.That(result, Is.EqualTo("Turbo-Pumpkin V"));
    }
}
