using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.Shared;

public class ForgeFlipService
{
    IForgeApi forgeApi;
    IPlayerStateApi stateApi;
    IProfileClient profileApi;

    public ForgeFlipService(IForgeApi forgeApi, IPlayerStateApi stateApi, IProfileClient profileApi)
    {
        this.forgeApi = forgeApi;
        this.stateApi = stateApi;
        this.profileApi = profileApi;
    }
    public async Task<IEnumerable<ForgeFlip>> GetForgeFlips(string mcName, string mcUuid, string profile = "current")
    {
        var extractedTask = stateApi.PlayerStatePlayerIdExtractedGetAsync(mcName);
        var forgeUnlockedTask = profileApi.GetForgeData(mcUuid, profile);
        var forgeFlips = await forgeApi.GetAllForgeAsync();
        if (mcUuid == null)
            return forgeFlips;
        var unlocked = await forgeUnlockedTask;
        var extractedInfo = await extractedTask;
        if (extractedInfo.HeartOfTheMountain?.Tier > 0)
            unlocked.HotMLevel = extractedInfo.HeartOfTheMountain.Tier;
        var result = new List<ForgeFlip>();
        foreach (var item in forgeFlips)
        {
            if (unlocked.HotMLevel < item.RequiredHotMLevel)
                continue;
            if (item.ProfitPerHour <= 0)
                continue;
            if (unlocked.QuickForgeSpeed != 0)
            {
                item.Duration = (int)((float)item.Duration * unlocked.QuickForgeSpeed);
            }
            if (item.ProfitPerHour > 1_000_000_000) // probably a calculation error, use daily volume instead
                item.ProfitPerHour = (item.CraftData.SellPrice - item.CraftData.CraftCost) * item.CraftData.Volume;
            result.Add(item);
        }
        return result.OrderByDescending(r => r.ProfitPerHour);
    }
}
