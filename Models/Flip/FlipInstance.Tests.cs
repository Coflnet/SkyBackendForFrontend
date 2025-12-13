using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Coflnet.Sky.Core;
using FluentAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class FlipInstanceTests
{
    [TestCase()]
    public void DerpyStart()
    {
        var derpyStart = new DateTime(2024, 8, 26, 7, 15, 1);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, new DateTime(2023, 1, 1)).Should().Be(2f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, new DateTime(2024, 8, 27)).Should().Be(5f);
        FlipInstance.GetFeeRateForStartingBid(100_000_000, new DateTime(2024, 8, 27)).Should().Be(6.5f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, derpyStart).Should().Be(5f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, derpyStart + TimeSpan.FromHours(123)).Should().Be(5f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, derpyStart + TimeSpan.FromHours(124.001)).Should().Be(2f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, new DateTime(2025, 1, 3)).Should().Be(2f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, new DateTime(2024, 12, 29)).Should().Be(5f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, new DateTime(2025, 5, 3)).Should().Be(5f);
        // Aurora event (Nov 22 - Dec 12, 2025) adds 1% tax
        FlipInstance.GetFeeRateForStartingBid(1_000_000, new DateTime(2025, 11, 25)).Should().Be(3f);
        FlipInstance.GetFeeRateForStartingBid(100_000_000, new DateTime(2025, 11, 25)).Should().Be(4.5f);
        FlipInstance.GetFeeRateForStartingBid(1_000_000, new DateTime(2025, 12, 26)).Should().Be(2f); // after aurora ends
    }
}