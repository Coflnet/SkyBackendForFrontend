using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Maps the collection display names used by the crafts service ("Melon Slice") to the collection ids
/// used as keys in the profile collection data ("MELON").
/// </summary>
public interface ICollectionNameResolver
{
    /// <summary>
    /// Resolves a collection display name to its id.
    /// </summary>
    /// <returns>the id or null if the name is unknown or the mapping is unavailable</returns>
    Task<string> GetCollectionId(string displayName);
}

/// <summary>
/// Loads the name to id mapping from the public hypixel resource and caches it.
/// </summary>
public class CollectionNameResolver : ICollectionNameResolver
{
    private const string ResourceUrl = "https://api.hypixel.net/v2/resources/skyblock/collections";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Names the game renamed that are no longer in the resource but still occur in the crafts data
    /// </summary>
    internal static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Acacia Wood", "LOG_2" },
        { "Dark Oak Wood", "LOG_2:1" },
        { "Jungle Wood", "LOG:3" },
        { "Spruce Wood", "LOG:1" },
        { "Oak Wood", "LOG" },
        { "Birch Wood", "LOG:2" },
        { "Clay", "CLAY_BALL" },
        { "Clownfish", "RAW_FISH:2" },
        { "Redstone", "REDSTONE" },
    };

    private readonly Func<Task<string>> loader;
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private Dictionary<string, string> names;
    private DateTime nextRefresh = DateTime.MinValue;

    public CollectionNameResolver() : this(LoadFromHypixel)
    {
    }

    /// <param name="loader">returns the json of the collections resource</param>
    public CollectionNameResolver(Func<Task<string>> loader)
    {
        this.loader = loader;
    }

    public async Task<string> GetCollectionId(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return null;
        if (Aliases.TryGetValue(displayName, out var alias))
            return alias;
        var map = await GetMap();
        return map != null && map.TryGetValue(displayName, out var id) ? id : null;
    }

    private async Task<Dictionary<string, string>> GetMap()
    {
        if (names != null && DateTime.UtcNow < nextRefresh)
            return names;
        await loadLock.WaitAsync();
        try
        {
            if (names != null && DateTime.UtcNow < nextRefresh)
                return names;
            try
            {
                var parsed = Parse(await loader());
                if (parsed.Count > 0)
                {
                    names = parsed;
                    nextRefresh = DateTime.UtcNow + Ttl;
                    return names;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not load collection names " + e.Message);
            }
            // keep the last good copy, retry soon
            nextRefresh = DateTime.UtcNow + RetryAfterFailure;
            return names;
        }
        finally
        {
            loadLock.Release();
        }
    }

    internal static Dictionary<string, string> Parse(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(json))
            return result;
        var collections = JObject.Parse(json)["collections"] as JObject;
        if (collections == null)
            return result;
        foreach (var category in collections.Properties())
        {
            if (category.Value["items"] is not JObject items)
                continue;
            foreach (var item in items.Properties())
            {
                var name = item.Value["name"]?.ToString();
                if (!string.IsNullOrEmpty(name))
                    result.TryAdd(name, item.Name);
            }
        }
        return result;
    }

    private static async Task<string> LoadFromHypixel()
    {
        var response = await new RestClient().ExecuteAsync(new RestRequest(ResourceUrl, Method.Get));
        if (!response.IsSuccessful)
            throw new Exception("Status " + response.StatusCode);
        return response.Content;
    }
}
