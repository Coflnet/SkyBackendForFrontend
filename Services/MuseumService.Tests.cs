using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace Coflnet.Sky.Commands.Shared;

public class MuseumServiceTests
{
    Core.Services.Item baseItem = new("xy", "111", "Tes Item", "b", "LEGENDARY", 0, null, null, 0, null, false, "TEST", null, null, null, null, null, null, "none", null, null, null,
            new(0, "ARMOR_SETS", new(), null, new() { { "XY", 4 } }, "INTERMEDIATE"), false, null, null, null, null);
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task ArmrorSetExclude()
    {
        var auctionMock = new Mock<IAuctionApi>();
        var itemService = new Mock<IHypixelItemStore>();
        itemService.Setup(service => service.GetItemsAsync()).ReturnsAsync(() => new Dictionary<string, Core.Services.Item>(){
            {"1", new CopyAble(baseItem){MuseumData=new(0, "ARMOR_SETS", new(), null, new() { { "GOBLIN", 4 } }, "INTERMEDIATE")}}
        });
        auctionMock.Setup(service => service.ApiAuctionLbinsGetAsync(0, default)).ReturnsAsync(() => new Dictionary<string, Sniper.Client.Model.ReferencePrice>(){
            {"1", new (){Price=100, AuctionId=1234}}
        });
        var service = new MuseumService(auctionMock.Object, NullLogger<MuseumService>.Instance, itemService.Object);
        var result = await service.GetBestOptions(new HashSet<string>(), 30);
        result.Count.Should().Be(1);

        result = await service.GetBestOptions(new HashSet<string>() { "GOBLIN" }, 30);
        result.Count.Should().Be(0);
    }

    [Test]
    public async Task ExtendSets()
    {
        var auctionMock = new Mock<IAuctionApi>();
        var itemService = new Mock<IHypixelItemStore>();
        itemService.Setup(service => service.GetItemsAsync()).ReturnsAsync(() => new Dictionary<string, Core.Services.Item>(){
            {"1", new CopyAble(baseItem){MuseumData=new(0, "ARMOR_SETS", new(), null, new() { { "CRIMSON_HUNTER", 4 } }, "INTERMEDIATE")}}
        });
        auctionMock.Setup(service => service.ApiAuctionLbinsGetAsync(0, default)).ReturnsAsync(() => new Dictionary<string, Sniper.Client.Model.ReferencePrice>(){
            {"1", new (){Price=100, AuctionId=1234}},
            {"BLAZE_BELT", new (){Price=100, AuctionId=1235}}
        });
        var service = new MuseumService(auctionMock.Object, NullLogger<MuseumService>.Instance, itemService.Object);
        var result = await service.GetBestOptions(new HashSet<string>(), 30);
        result.First().Value.auctionid.Length.Should().Be(2);
    }

    [Test]
    public async Task ExcludesChildIfParentDonated()
    {
        var auctionMock = new Mock<IAuctionApi>();
        var itemService = new Mock<IHypixelItemStore>();
        itemService.Setup(service => service.GetItemsAsync()).ReturnsAsync(() => new Dictionary<string, Core.Services.Item>(){
            {"SKYMART_VACUUM", new CopyAble(baseItem){Id="SKYMART_VACUUM", MuseumData=new(2, "RARITIES", new(){{"SKYMART_VACUUM","SKYMART_TURBO_VACUUM"}}, null, null, "AMATEUR")}},
            {"SKYMART_TURBO_VACUUM", new CopyAble(baseItem){Id="SKYMART_TURBO_VACUUM",MuseumData=new(2, "RARITIES", new(){{"SKYMART_TURBO_VACUUM","SKYMART_HYPER_VACUUM"}}, null, null, "AMATEUR")}},
            {"SKYMART_HYPER_VACUUM", new CopyAble(baseItem){Id="SKYMART_HYPER_VACUUM",MuseumData=new(4, "RARITIES", new(){{"SKYMART_HYPER_VACUUM","INFINI_VACUUM"}}, null, null, "INTERMEDIATE")}},
            {"INFINI_VACUUM", new CopyAble(baseItem){Id="INFINI_VACUUM",MuseumData=new(6, "RARITIES", new(){{"INFINI_VACUUM","INFINI_VACUUM_HOOVERIUS"}}, null, null, "SKILLED")}},
            {"INFINI_VACUUM_HOOVERIUS", new CopyAble(baseItem){Id="INFINI_VACUUM_HOOVERIUS",MuseumData=new(8, "RARITIES", new(), null, null, "EXPERT")} }
        });
        auctionMock.Setup(service => service.ApiAuctionLbinsGetAsync(0, default)).ReturnsAsync(() => new Dictionary<string, Sniper.Client.Model.ReferencePrice>(){
            {"SKYMART_VACUUM", new (){Price=100, AuctionId=1234}},
            {"SKYMART_TURBO_VACUUM", new (){Price=100, AuctionId=1235}},
            {"SKYMART_HYPER_VACUUM", new (){Price=100, AuctionId=1235}},
            {"INFINI_VACUUM", new (){Price=100, AuctionId=1235}},
            {"INFINI_VACUUM_HOOVERIUS", new (){Price=100, AuctionId=1235}}
        });
        var service = new MuseumService(auctionMock.Object, NullLogger<MuseumService>.Instance, itemService.Object);
        var result = await service.GetBestOptions(new HashSet<string>(), 30);
        result.Count.Should().Be(5);
        result.First().Value.pricePerExp.Should().Be(4);
        result.First().Key.Should().Be("INFINI_VACUUM_HOOVERIUS"); // best option all donated

        result = await service.GetBestOptions(new HashSet<string>() { "SKYMART_HYPER_VACUUM" }, 30);
        result.Count.Should().Be(2);
        result.First().Value.pricePerExp.Should().Be(7);

        result = await service.GetBestOptions(new HashSet<string>() { "INFINI_VACUUM_HOOVERIUS" }, 30);
        result.Count.Should().Be(0);
    }

    public record CopyAble : Core.Services.Item
    {
        public CopyAble(Core.Services.Item item) : base(item)
        {
        }


    }
}