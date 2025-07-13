using System.Collections.Generic;
using Coflnet.Sky.Core;
using FluentAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class AverageTimeToSellTests
{
    [TestCase("1d", 1, true)]
    [TestCase("<1d", 2, true)]
    [TestCase("<2h", 13, true)]
    [TestCase(">2h", 13, false)]
    [TestCase(">2.5h", 13, false)]
    [TestCase(">2d", 2, false)]
    [TestCase(">2w", 0.1f, false)]
    [TestCase(">1w", 0.1f, true)]
    public void MatchesCases(string input, float volume, bool expected)
    {
        var filter = new AverageTimeToSellDetailedFlipFilter();
        Assert.That(filter.GetExpression(null, input).Compile()(new FlipInstance() { Volume = volume }), Is.EqualTo(expected));
    }

    [TestCase("<1h", 1, true)]
    [TestCase("<1h", 61, false)]
    [TestCase("1h-2h", 61, true)]
    [TestCase("1m-12m", 9, true)]
    public void ActualTimeToSell(string input, float time, bool expected)
    {
        var filter = new AverageTimeToSellDetailedFlipFilter();
        var flip = new FlipInstance() { Context = new Dictionary<string, string> { { "minToSell", time.ToString() } } };
        filter.GetExpression(null, input).Compile()(flip).Should().Be(expected);
    }

    [TestCase("1x")]
    [TestCase("1")]
    [TestCase("111")]
    public void ErrorMessage(string input)
    {
        var filter = new AverageTimeToSellDetailedFlipFilter();
        Assert.Throws<CoflnetException>(() => filter.GetExpression(null, input));
    }
}