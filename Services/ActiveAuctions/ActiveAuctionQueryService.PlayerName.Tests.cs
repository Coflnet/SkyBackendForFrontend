using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class ActiveAuctionQueryServicePlayerNameTests
{
    private static List<ActiveAuctionPreview> Page() => new()
    {
        new() { Uuid = "a1", AuctioneerId = "s1" },
        new() { Uuid = "a2", AuctioneerId = "s2" },
        new() { Uuid = "a3", AuctioneerId = "s1" },
    };

    private static ActiveAuctionQueryService Create(IActiveAuctionNameSource source)
        => new(null, null, null, null, source);

    [Test]
    public async Task OneBatchCallFillsNames()
    {
        var source = new Mock<IActiveAuctionNameSource>();
        source.Setup(s => s.GetNamesAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, string> { ["s1"] = "Alice", ["s2"] = "Bob" });
        var items = Page();
        var service = Create(source.Object);

        await service.FillPlayerNamesAsync(items);

        source.Verify(s => s.GetNamesAsync(It.Is<IReadOnlyCollection<string>>(c => c.Count == 2)), Times.Once);
        items.Select(i => i.PlayerName).Should().Equal("Alice", "Bob", "Alice");

        await service.FillPlayerNamesAsync(Page());
        source.Verify(s => s.GetNamesAsync(It.IsAny<IReadOnlyCollection<string>>()), Times.Once, "cached");
    }

    [Test]
    public async Task ThrowingSourceLeavesNamesNull()
    {
        var source = new Mock<IActiveAuctionNameSource>();
        source.Setup(s => s.GetNamesAsync(It.IsAny<IReadOnlyCollection<string>>())).ThrowsAsync(new Exception("down"));
        var items = Page();

        await Create(source.Object).FillPlayerNamesAsync(items);

        items.Should().HaveCount(3);
        items.Should().OnlyContain(i => i.PlayerName == null);
    }
}
