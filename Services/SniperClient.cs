using System.Linq;
using Newtonsoft.Json;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Sniper.Client.Api;
using System;
using Microsoft.Extensions.Logging;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Coflnet.Sky.Commands.Shared;

public interface ISniperClient
{
    Task<List<Sniper.Client.Model.PriceEstimate>> GetPrices(IEnumerable<SaveAuction> auctionRepresent, bool includeSelfLearning = true);
    Task<Dictionary<string, long>> GetCleanPrices();
}

public class SniperClient : ISniperClient, IDisposable
{
    private readonly HttpClient sniperClient;
    private readonly ILogger<SniperClient> logger;
    private readonly ISniperApi sniperApi;
    private readonly MemoryCache priceCache = new(new MemoryCacheOptions { SizeLimit = 128 });
    private static readonly TimeSpan FreshCacheDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaleCacheDuration = TimeSpan.FromSeconds(30);

    private sealed record CacheEntry(DateTimeOffset CreatedAt, List<Sniper.Client.Model.PriceEstimate> Prices);

    public SniperClient(HttpClient sniperClient, ISniperApi sniperApi, ILogger<SniperClient> logger)
    {
        this.sniperClient = sniperClient;
        this.sniperApi = sniperApi;
        this.logger = logger;
    }

    public async Task<List<Sniper.Client.Model.PriceEstimate>> GetPrices(IEnumerable<SaveAuction> auctionRepresent, bool includeSelfLearning = true)
    {
        var auctions = auctionRepresent.ToList();
        var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);
        var body = MessagePackSerializer.Serialize(auctions, options);
        var cacheKey = $"{includeSelfLearning}:{Convert.ToHexString(SHA256.HashData(body))}";
        priceCache.TryGetValue<CacheEntry>(cacheKey, out var cached);
        if (cached != null && DateTimeOffset.UtcNow - cached.CreatedAt <= FreshCacheDuration)
            return cached.Prices;

        using var timeout = sniperClient.Timeout == Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource()
            : new CancellationTokenSource(sniperClient.Timeout);
        try
        {
            using var response = await SendPriceRequest(body, includeSelfLearning, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var prices = JsonConvert.DeserializeObject<List<Sniper.Client.Model.PriceEstimate>>(content)
                ?? auctions.Select(_ => new Sniper.Client.Model.PriceEstimate()).ToList();
            if (prices.Count != auctions.Count)
            {
                logger.LogError("Sniper returned {PriceCount} prices for {AuctionCount} auctions", prices.Count, auctions.Count);
                prices = auctions.Select((_, index) => prices.ElementAtOrDefault(index) ?? new Sniper.Client.Model.PriceEstimate()).ToList();
            }
            priceCache.Set(cacheKey, new CacheEntry(DateTimeOffset.UtcNow, prices), new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = StaleCacheDuration,
                Size = 1
            });
            return prices;
        }
        catch (OperationCanceledException) when (cached != null)
        {
            logger.LogWarning("Sniper request timed out; returning a cached result");
            return cached.Prices;
        }
        catch (OperationCanceledException e)
        {
            logger.LogError(e, "Sniper request exceeded the HTTP client timeout");
            return auctions.Select(_ => new Sniper.Client.Model.PriceEstimate()).ToList();
        }
        catch (HttpRequestException e) when (cached != null)
        {
            logger.LogWarning(e, "Sniper request failed; returning a cached result");
            return cached.Prices;
        }
        catch (HttpRequestException e)
        {
            logger.LogError(e, "Sniper service could not be reached");
            return auctions.Select(_ => new Sniper.Client.Model.PriceEstimate()).ToList();
        }
    }

    private async Task<HttpResponseMessage> SendPriceRequest(byte[] body, bool includeSelfLearning, CancellationToken cancellationToken)
    {
        var query = $"includeSelfLearning={includeSelfLearning.ToString().ToLowerInvariant()}";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sniper/prices/messagepack?{query}");
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-msgpack");
        var response = await sniperClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            return response;

        response.Dispose();
        using var legacyRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sniper/prices?{query}")
        {
            Content = new StringContent(JsonConvert.SerializeObject(Convert.ToBase64String(body)), Encoding.UTF8, "application/json")
        };
        return await sniperClient.SendAsync(legacyRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, long>> GetCleanPrices()
    {
        return await sniperApi.ApiSniperPricesCleanGetAsync();
    }

    public void Dispose() => priceCache.Dispose();

    public static (double, bool fromMedian) InstaSellPrice(Sniper.Client.Model.PriceEstimate pricing)
    {
        var deduct = 0.12;
        if (pricing.Median < 15_000_000)
            deduct = 0.18;
        if (pricing.Median > 150_000_000)
            deduct = 0.10;
        if (pricing.Volume < 1)
            deduct += 0.05;
        if (pricing.Volume < 0.15)
            deduct += 0.05;
        var fromMed = pricing.Median * (1 - deduct);
        var target = Math.Max(fromMed, Math.Min(pricing.Lbin.Price * (1 - deduct - 0.08), fromMed * 1.2));
        if (pricing.ItemKey != pricing.LbinKey)
            if (pricing.MedianKey == pricing.ItemKey || pricing.Lbin.Price == 0)
                target = fromMed;
            else
                target = Math.Min(fromMed, pricing.Lbin.Price * (1 - deduct - 0.08));
        return (target, fromMed == target);
    }
}
