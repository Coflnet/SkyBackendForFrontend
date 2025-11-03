using System.Linq;
using Newtonsoft.Json;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using System.Threading.Tasks;
using RestSharp;
using Coflnet.Sky.Sniper.Client.Api;
using System;
using Microsoft.Extensions.Logging;
using MessagePack;

namespace Coflnet.Sky.Commands.Shared;

public interface ISniperClient
{
    Task<List<Sniper.Client.Model.PriceEstimate>> GetPrices(IEnumerable<SaveAuction> auctionRepresent, bool includeSelfLearning = true);
    Task<Dictionary<string, long>> GetCleanPrices();
}

public class SniperClient : ISniperClient
{
    private RestSharp.RestClient sniperClient;
    private readonly ILogger<SniperClient> logger;
    private readonly ISniperApi sniperApi;

    public SniperClient(ISniperApi sniperApi, ILogger<SniperClient> logger)
    {
        sniperClient = new(sniperApi.GetBasePath());
        this.sniperApi = sniperApi;
        this.logger = logger;
    }
    public async Task<List<Sniper.Client.Model.PriceEstimate>> GetPrices(IEnumerable<SaveAuction> auctionRepresent, bool includeSelfLearning = true)
    {
        var request = new RestRequest("/api/sniper/prices", RestSharp.Method.Post);
        var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);
        request.AddJsonBody(JsonConvert.SerializeObject(Convert.ToBase64String(MessagePackSerializer.Serialize(auctionRepresent, options))));
        if (includeSelfLearning)
            request.AddParameter("includeSelfLearning", "true", ParameterType.QueryString);

        var respone = await sniperClient.ExecuteAsync(request).ConfigureAwait(false);
        if (respone.StatusCode == 0)
        {
            logger.LogError("sniper service could not be reached");
            return auctionRepresent.Select(a => new Sniper.Client.Model.PriceEstimate()).ToList();
        }
        try
        {
            return JsonConvert.DeserializeObject<List<Sniper.Client.Model.PriceEstimate>>(respone.Content);
        }
        catch (System.Exception)
        {
            logger.LogError("responded with " + respone.StatusCode + respone.Content);
            throw;
        }
    }

    public async Task<Dictionary<string, long>> GetCleanPrices()
    {
        return await sniperApi.ApiSniperPricesCleanGetAsync();
    }

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