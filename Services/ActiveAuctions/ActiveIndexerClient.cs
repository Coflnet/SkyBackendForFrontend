using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using MessagePack;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>State of the indexer's active auction set</summary>
public record ActiveStats(int Count, bool Ready, DateTime? SnapshotAt, long Version);

/// <summary>Reads the active auction set of SkyIndexer</summary>
public interface IActiveIndexerClient
{
    /// <summary>Streams all active auctions, throws <see cref="HttpRequestException"/> while the indexer is not ready</summary>
    IAsyncEnumerable<SaveAuction> StreamAllAsync(CancellationToken ct);
    /// <summary>UId to End ticks of all active auctions, null while the indexer is not ready</summary>
    Task<Dictionary<long, long>> GetUidEndTicksAsync(CancellationToken ct);
    /// <summary>A single active auction, null if unknown</summary>
    Task<SaveAuction> GetAuctionAsync(long uid, CancellationToken ct);
    Task<ActiveStats> GetStatsAsync(CancellationToken ct);
}

public class ActiveIndexerClient : IActiveIndexerClient
{
    private readonly HttpClient http;

    public ActiveIndexerClient(HttpClient http, IConfiguration config)
    {
        this.http = http;
        if (http.BaseAddress == null)
            http.BaseAddress = new Uri(config["INDEXER_BASE_URL"].TrimEnd('/') + "/");
        if (http.Timeout < TimeSpan.FromMinutes(10))
            http.Timeout = TimeSpan.FromMinutes(10); // the full export is large
    }

    public async IAsyncEnumerable<SaveAuction> StreamAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await http.GetAsync("active/auctions", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (var a in ActiveAuctionSetSerializer.DeserializeAsync(stream, ct))
            yield return a;
    }

    public async Task<Dictionary<long, long>> GetUidEndTicksAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("active/uids", ct);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return null;
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return MessagePackSerializer.Deserialize<Dictionary<long, long>>(bytes, ActiveAuctionSetSerializer.Options, ct);
    }

    public async Task<SaveAuction> GetAuctionAsync(long uid, CancellationToken ct)
    {
        using var response = await http.GetAsync($"active/auctions/{uid}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return null;
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var record = MessagePackSerializer.Deserialize<ActiveAuctionRecord>(bytes, ActiveAuctionSetSerializer.Options, ct);
        if (record?.Auction != null)
            record.Auction.ItemId = record.ItemId;
        return record?.Auction;
    }

    public async Task<ActiveStats> GetStatsAsync(CancellationToken ct)
    {
        return await http.GetFromJsonAsync<ActiveStats>("active/stats", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), ct);
    }
}
