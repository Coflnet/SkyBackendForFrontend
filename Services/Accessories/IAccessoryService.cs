using System.Collections.Generic;
using System.Threading.Tasks;

namespace Coflnet.Sky.Commands.Shared;

public interface IAccessoryService
{
    /// <summary>Merges the profile talisman bag and the opened accessory bag, failed sources count as empty</summary>
    Task<OwnedAccessories> GetOwnedAccessories(string mcUuid, string profileId);
    /// <summary>Not yet owned accessories ordered by lowest bin per magical power gained</summary>
    Task<List<CheapAccessory>> GetCheapestPerMagicalPower(string mcUuid, string profileId, int limit);
    Task<List<CheapAccessory>> GetCheapestPerMagicalPower(OwnedAccessories owned, int limit);
}
