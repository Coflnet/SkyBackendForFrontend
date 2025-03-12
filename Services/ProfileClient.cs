using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Commands.Shared;

public interface IProfileClient
{
    Task<ProfileClient.ForgeData> GetForgeData(string playerId, string profile);
    Task<List<ProfitableCraft>> FilterProfitableCrafts(Task<List<ProfitableCraft>> craftsTask, string playerId, string profileId);
    Task<Dictionary<string, ProfileClient.CollectionElem>> GetCollectionData(string playerId, string profile);
    Task<Dictionary<string, ProfileClient.SlayerElem>> GetSlayerData(string playerId, string profile);
    Task<HashSet<string>> GetAlreadyDonatedToMuseum(string playerId, string profile, DateTime maxAge);
    Task<Dictionary<string, string>> GetProfiles(string playerId);
    Task<Api.Client.Model.Member> GetProfile(string playerId, string profile, DateTime maxAge);
    Task<string> GetActiveProfileId(string playerId);
}

public class ProfileClient : IProfileClient
{
    private RestClient profileClient = null;
    public ProfileClient(IConfiguration config) =>
                profileClient = new RestClient(config["PROFILE_BASE_URL"]);


    public async Task<ForgeData> GetForgeData(string playerId, string profile)
    {
        var request = new RestRequest($"api/profile/{playerId}/{profile}/data/forge", Method.Get);
        var response = await profileClient.ExecuteAsync<ForgeData>(request);
        return response.Data;
    }

    public async Task<Dictionary<string, CollectionElem>> GetCollectionData(string playerId, string profile)
    {
        var collectionJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{playerId}/{profile}/data/collections"));
        var collection = JsonConvert.DeserializeObject<Dictionary<string, CollectionElem>>(collectionJson.Content);
        return collection;
    }

    public async Task<Dictionary<string, SlayerElem>> GetSlayerData(string playerId, string profile)
    {
        var slayerJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{playerId}/{profile}/data/slayers"));
        var slayer = JsonConvert.DeserializeObject<Dictionary<string, SlayerElem>>(slayerJson.Content);
        return slayer;
    }
    public async Task<HashSet<string>> GetAlreadyDonatedToMuseum(string playerId, string profile, DateTime maxAge)
    {
        var isoTime = maxAge.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var museumJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{playerId}/{profile}/museum?maxAge={isoTime}"));
        var donated = JsonConvert.DeserializeObject<DonatedToMuseum>(museumJson.Content);
        if (donated == null)
            return new HashSet<string>();
        return [.. donated.Items.Keys];
    }

    public async Task<List<ProfitableCraft>> FilterProfitableCrafts(Task<List<ProfitableCraft>> craftsTask, string playerId, string profileId)
    {
        var collectionTask = GetCollectionData(playerId, profileId);
        var slayers = await GetSlayerData(playerId, profileId);
        var collection = await collectionTask;
        var crafts = await craftsTask;
        var list = new List<ProfitableCraft>();
        foreach (var item in crafts)
        {
            if (item == null)
                continue;

            if (item.ReqCollection == null
            || collection.TryGetValue(item.ReqCollection.Name, out CollectionElem elem)
                    && elem.tier >= item.ReqCollection.Level)
            {
                list.Add(item);
            }
            else if (item.ReqSlayer == null
                || slayers.TryGetValue(item.ReqSlayer.Name.ToLower(), out SlayerElem slayerElem)
                  && slayerElem.Level.currentLevel >= item.ReqSlayer.Level)
                list.Add(item);
        }
        return list;
    }

    public async Task<Dictionary<string, string>> GetProfiles(string playerId)
    {
        var museumJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{playerId}"));
        var profiles = JsonConvert.DeserializeObject<PlayerProfiles>(museumJson.Content);
        return profiles.Profiles;
    }

    public async Task<Api.Client.Model.Member> GetProfile(string playerId, string profile, DateTime maxAge)
    {
        var museumJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{playerId}/{profile}?maxAge={maxAge:yyyy-MM-ddTHH:mm:ssZ}"));
        var profiles = JsonConvert.DeserializeObject<Api.Client.Model.Member>(museumJson.Content);
        return profiles;
    }

    public async Task<string> GetActiveProfileId(string playerId)
    {
        return (await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{playerId}/active"))).Content.Replace("-", "");
    }

    public class PlayerProfiles
    {
        public Dictionary<string, string> Profiles;
    }

    public class ForgeData
    {
        public int HotMLevel { get; set; }
        public float QuickForgeSpeed { get; set; }
        public Dictionary<string, int> CollectionLevels { get; set; }
    }

    public class SlayerElem
    {
        public SlayerLevel Level { get; set; }
        public class SlayerLevel
        {
            public int currentLevel;
        }
    }

    public class CollectionElem
    {
        /// <summary>
        /// The collection tier/level this requires
        /// </summary>
        public int tier;
    }

    public class DonatedToMuseum
    {
        public long Value;
        public bool Appraisal;
        public Dictionary<string, object> Items;
    }
}

