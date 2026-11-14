using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerName.Client.Api;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>Resolves player uuids to names in one batch</summary>
public interface IActiveAuctionNameSource
{
    Task<Dictionary<string, string>> GetNamesAsync(IReadOnlyCollection<string> uuids);
}

/// <summary>Batch lookup via the SkyPlayerName service (POST names/batch)</summary>
public class PlayerNameApiNameSource : IActiveAuctionNameSource
{
    private readonly IPlayerNameApi api;

    public PlayerNameApiNameSource(IPlayerNameApi api)
    {
        this.api = api;
    }

    public Task<Dictionary<string, string>> GetNamesAsync(IReadOnlyCollection<string> uuids)
        => api.PlayerNameNamesBatchPostAsync(uuids.ToList());
}
