using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.Shared;

public class ActiveAuctionFilterService
{
    private const int MaxLimit = 500;
    private readonly FilterEngine filterEngine;

    public ActiveAuctionFilterService(FilterEngine filterEngine)
    {
        this.filterEngine = filterEngine;
    }

    public async Task<IReadOnlyList<ActiveAuctionPreview>> QueryPreviews(
        Dictionary<string, string> filters,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = new ActiveAuctionsContext();
        var query = BuildFilteredQuery(context, filters, limit, offset);
        return await query
            .Select(auction => new ActiveAuctionPreview
            {
                Uuid = auction.Uuid,
                UId = auction.UId,
                Tag = auction.Tag,
                ItemName = auction.ItemName,
                StartingBid = auction.StartingBid,
                HighestBidAmount = auction.HighestBidAmount,
                End = auction.End,
                Bin = auction.Bin,
                Tier = auction.Tier,
                Category = auction.Category,
                AuctioneerId = auction.AuctioneerId
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SaveAuction>> QueryAuctions(
        Dictionary<string, string> filters,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = new ActiveAuctionsContext();
        var query = BuildFilteredQuery(context, filters, limit, offset, includeDetails: true);

        return await query.ToListAsync(cancellationToken);
    }

    private IQueryable<SaveAuction> BuildFilteredQuery(ActiveAuctionsContext context, Dictionary<string, string> filters, int limit, int offset, bool includeDetails = false)
    {
        if (!ActiveAuctionsContext.IsConfigured)
            throw new CoflnetException("active_auction_lookup_disabled", "Active auction lookup is not configured");

        filters ??= new Dictionary<string, string>();
        limit = Math.Clamp(limit, 1, MaxLimit);
        offset = Math.Max(0, offset);

        var now = DateTime.UtcNow;
        var query = context.Auctions
            .AsNoTracking()
            .Where(auction => auction.End > now);

        query = ApplyItemIdFilter(query, filters);
        query = filterEngine.AddFilters(query, filters);

        if (includeDetails)
        {
            query = query
                .Include(auction => auction.NBTLookup)
                .Include(auction => auction.Enchantments)
                .AsSplitQuery();
        }

        return query
            .OrderBy(auction => auction.End)
            .ThenBy(auction => auction.Id)
            .Skip(offset)
            .Take(limit);
    }

    private static IQueryable<SaveAuction> ApplyItemIdFilter(IQueryable<SaveAuction> query, IReadOnlyDictionary<string, string> filters)
    {
        if (!filters.TryGetValue("ItemId", out var itemIdFilter) || string.IsNullOrWhiteSpace(itemIdFilter))
            return query;

        var itemIds = itemIdFilter
            .Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var itemId) ? itemId : 0)
            .Where(itemId => itemId > 0)
            .Distinct()
            .ToArray();

        return itemIds.Length == 0 ? query : query.Where(auction => itemIds.Contains(auction.ItemId));
    }
}

public class ActiveAuctionPreview
{
    public string Uuid { get; set; }
    public long UId { get; set; }
    public string Tag { get; set; }
    public string ItemName { get; set; }
    public long StartingBid { get; set; }
    public long HighestBidAmount { get; set; }
    public DateTime End { get; set; }
    public bool Bin { get; set; }
    public Tier Tier { get; set; }
    public Category Category { get; set; }
    public string AuctioneerId { get; set; }
}