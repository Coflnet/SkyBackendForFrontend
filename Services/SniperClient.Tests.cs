using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Sniper.Client.Api;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class SniperClientTests
{
    [Test]
    public async Task GetPricesSendsMessagePackAndCachesIdenticalRequest()
    {
        var expected = new List<Sniper.Client.Model.PriceEstimate> { new() { Median = 123 } };
        var handler = new RecordingHandler(JsonConvert.SerializeObject(expected));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://sniper") };
        var client = new SniperClient(httpClient, Mock.Of<ISniperApi>(), NullLogger<SniperClient>.Instance);
        var auctions = new List<SaveAuction> { new() { Tag = "TEST" } };

        var first = await client.GetPrices(auctions, false);
        var second = await client.GetPrices(auctions, false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Single().Median, Is.EqualTo(123));
            Assert.That(second.Single().Median, Is.EqualTo(123));
            Assert.That(handler.CallCount, Is.EqualTo(1));
            Assert.That(handler.ContentType, Is.EqualTo("application/x-msgpack"));
            Assert.That(handler.Path, Does.StartWith("/api/sniper/prices/messagepack"));
        });
        var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);
        Assert.That(MessagePackSerializer.Deserialize<List<SaveAuction>>(handler.Body, options), Has.Count.EqualTo(1));
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public byte[] Body { get; private set; }
        public string ContentType { get; private set; }
        public string Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            ContentType = request.Content.Headers.ContentType?.MediaType;
            Path = request.RequestUri.PathAndQuery;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
