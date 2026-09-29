using System.Collections.Generic;
using Coflnet.Sky.Api.Models.Mod;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class SettingsUpdaterTests
{
    [Test]
    public void ClearWithNameRemovesOnlyThatEntry()
    {
        var settings = new DescriptionSetting { DisableInfoIn = new HashSet<string> { "TradeInfoDisplay", "OtherDisplay" } };

        var result = SettingsUpdater.UpdateValueOnObject("clear TradeInfoDisplay", nameof(DescriptionSetting.DisableInfoIn), settings);

        Assert.That(result, Is.Null);
        Assert.That(settings.DisableInfoIn, Is.EquivalentTo(new[] { "OtherDisplay" }));
        Assert.That(settings.DisableInfoIn, Does.Not.Contain("clear TradeInfoDisplay"));
    }

    [Test]
    public void ClearWithNameRemovesCaseInsensitively()
    {
        var settings = new DescriptionSetting { DisableInfoIn = new HashSet<string> { "TradeInfoDisplay" } };

        SettingsUpdater.UpdateValueOnObject("clear tradeinfodisplay", nameof(DescriptionSetting.DisableInfoIn), settings);

        Assert.That(settings.DisableInfoIn, Is.Empty);
    }

    [Test]
    public void RmRemovesCaseInsensitively()
    {
        var settings = new DescriptionSetting { DisableInfoIn = new HashSet<string> { "TradeInfoDisplay" } };

        SettingsUpdater.UpdateValueOnObject("rm tradeinfodisplay", nameof(DescriptionSetting.DisableInfoIn), settings);

        Assert.That(settings.DisableInfoIn, Is.Empty);
    }

    [Test]
    public void BareClearWipesEntireSet()
    {
        var settings = new DescriptionSetting { DisableInfoIn = new HashSet<string> { "TradeInfoDisplay", "OtherDisplay" } };

        var result = SettingsUpdater.UpdateValueOnObject("clear", nameof(DescriptionSetting.DisableInfoIn), settings);

        Assert.That(result, Is.Null);
        Assert.That(settings.DisableInfoIn, Is.Empty);
    }

    [Test]
    public void PlainAddStillWorks()
    {
        var settings = new DescriptionSetting { DisableInfoIn = new HashSet<string>() };

        var result = SettingsUpdater.UpdateValueOnObject("TradeInfoDisplay", nameof(DescriptionSetting.DisableInfoIn), settings);

        Assert.That(result, Is.EqualTo("TradeInfoDisplay"));
        Assert.That(settings.DisableInfoIn, Is.EquivalentTo(new[] { "TradeInfoDisplay" }));
    }

    [Test]
    public void UpdateCleansUpPreExistingJunkEntries()
    {
        var settings = new DescriptionSetting
        {
            DisableInfoIn = new HashSet<string> { "clear TradeInfoDisplay", "TradeInfoDisplay", "bazaar" }
        };

        // updating an unrelated entry should also purge junk already stored in the set
        SettingsUpdater.UpdateValueOnObject("newEntry", nameof(DescriptionSetting.DisableInfoIn), settings);

        Assert.That(settings.DisableInfoIn, Is.EquivalentTo(new[] { "bazaar", "newEntry" }));
    }

    [Test]
    public void CleanupJunkEntriesTurnsMixedJunkIntoCleanSet()
    {
        var set = new HashSet<string> { "clear TradeInfoDisplay", "clear ListPriceRecommend", "TradeInfoDisplay", "bazaar" };

        var changed = SettingsUpdater.CleanupJunkEntries(set);

        Assert.That(changed, Is.True);
        Assert.That(set, Is.EquivalentTo(new[] { "bazaar" }));
    }

    [Test]
    public void CleanupJunkEntriesHandlesNullSet()
    {
        Assert.DoesNotThrow(() => SettingsUpdater.CleanupJunkEntries(null));
    }

    [Test]
    public void CleanupJunkEntriesReturnsFalseWhenNothingToClean()
    {
        var set = new HashSet<string> { "bazaar" };

        var changed = SettingsUpdater.CleanupJunkEntries(set);

        Assert.That(changed, Is.False);
        Assert.That(set, Is.EquivalentTo(new[] { "bazaar" }));
    }
}
