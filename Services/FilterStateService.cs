using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Model;
using Coflnet.Sky.Mayor.Client.Model;
using dev;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Commands.Shared;
public class FilterStateService
{
    public class FilterState
    {

        public ConcurrentDictionary<ItemCategory, HashSet<string>> itemCategories { get; set; } = new();
        public string CurrentMayor { get; set; }
        public string NextMayor { get; set; }
        public string PreviousMayor { get; set; }
        public DateTime LastUpdate { get; set; }
        public Dictionary<int, HashSet<string>> IntroductionAge { get; set; } = new();
        public HashSet<string> ExistingTags { get; set; } = new();
        public HashSet<string> CurrentPerks { get; set; } = new();
        public HashSet<string> NextPerks { get; set; } = new();
    }

    private SemaphoreSlim updateLock = new SemaphoreSlim(1, 1);

    public FilterState State { get; set; } = new FilterState();

    private Sky.Mayor.Client.Api.IMayorApi mayorApi;
    private Items.Client.Api.IItemsApi itemsApi;
    private ILogger<FilterStateService> logger;

    public FilterStateService(ILogger<FilterStateService> logger, Sky.Mayor.Client.Api.IMayorApi mayorApi, Items.Client.Api.IItemsApi itemsApi)
    {
        this.logger = logger;
        this.mayorApi = mayorApi;
        this.itemsApi = itemsApi;
    }

    public async Task UpdateState()
    {
        if (DateTime.Now - State.LastUpdate > TimeSpan.FromHours(1))
        {
            State.LastUpdate = DateTime.Now;
        }
        else
            return;
        try
        {
            State.PreviousMayor = mayorApi.MayorLastGet().ToLower();
            State.NextMayor = (await mayorApi.MayorNextGetAsync())?.Name?.ToLower();
            UpdateCurrentPerks();
            logger.LogInformation("Current mayor is {current}, perks: {perks}", State.CurrentMayor, string.Join(", ", State.CurrentPerks));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load mayor");
        }
        foreach (var item in State.itemCategories.Keys)
        {
            try
            {
                await GetItemCategory(item);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not load item category {0}", item);
            }
        }
        /*
         Don't refresh to not introduce new tags as known that would be filtered
         var items = await itemsApi.ItemNamesGetAsync();
         foreach (var item in items.Select(i => i.Tag))
         {
             State.ExistingTags.Add(item);
         }*/
        foreach (var item in State.IntroductionAge)
        {
            if (item.Key <= 1)
                continue;
            var newItems = await itemsApi.ItemsRecentGetAsync(item.Key);
            if (newItems == null)
            {
                logger.LogError("Could not load new items from {0} days", item.Key);
                continue;
            }
            item.Value.Clear();
            foreach (var newItem in newItems)
            {
                item.Value.Add(newItem);
            }
        }
        logger.LogInformation("Loaded {0} item tags", State.ExistingTags.Count);
    }

    private void UpdateCurrentPerks()
    {
        var restsharp = new RestClient("https://api.hypixel.net");
        var mayorResponse = restsharp.Execute(new RestRequest("/v2/resources/skyblock/election"));
        var mayors = JsonConvert.DeserializeObject<MayorResponse>(mayorResponse.Content);
        if (!mayors.success)
        {
            Console.WriteLine("Could not load mayor perks");
            return;
        }
        var mayor = mayors.mayor.perks.Select(p => p.name).ToList();
        var minister = mayors.mayor?.minister?.perk?.name;
        State.CurrentPerks = new HashSet<string>(mayor);
        if (minister != null)
            State.CurrentPerks.Add(minister);
        State.CurrentMayor = mayors.mayor.name.ToLower();
        var currentElection = mayors.current;
        if (currentElection == null)
        {
            State.NextPerks = [];
            return;
        }
        if (currentElection.candidates.All(c => c.votes == 0))
        {
            // on hidden elections all perks may be possible 
            // https://discord.com/channels/267680588666896385/1369933748501610506
            State.NextPerks.Clear();
            foreach (var candidate in currentElection.candidates)
            {
                foreach (var perk in candidate.perks)
                {
                    State.NextPerks.Add(perk.name);
                }
            }
            return;
        }
        var probableWinner = currentElection.candidates
            .OrderByDescending(c => c.votes)
            .FirstOrDefault().perks.Select(p => p.name).ToList();
        State.NextPerks.Clear();
        foreach (var perk in probableWinner)
        {
            State.NextPerks.Add(perk);
        }
        var runnerUp = currentElection.candidates
            .OrderByDescending(c => c.votes)
            .Skip(1)
            .FirstOrDefault().perks.Where(p => p.minister).Select(p => p.name).FirstOrDefault();
        if (runnerUp != null)
            State.NextPerks.Add(runnerUp);
    }

    public async Task GetItemCategory(ItemCategory category)
    {
        var items = await itemsApi.ItemsCategoryCategoryItemsGetAsync(category);
        if (!State.itemCategories.ContainsKey(category))
            State.itemCategories[category] = new HashSet<string>();
        if (items == null)
        {
            Activity.Current?.AddTag("error", "could_not_load");
            throw new CoflnetException("could_not_load", $"Could not load items for category {category}");
        }
        foreach (var item in items)
        {
            State.itemCategories[category].Add(item);
        }
    }

    public HashSet<string> GetIntroductionAge(int days)
    {
        if (!State.IntroductionAge.ContainsKey(days))
        {
            var items = itemsApi.ItemsRecentGetAsync(days).Result;
            if (items == null && days == 1)
                return new HashSet<string>(); // handled via known tags
            if (items == null)
            {
                Activity.Current?.AddTag("error", "could_not_load");
                throw new CoflnetException("could_not_load", $"Could not load new items from {days} days");
            }
            State.IntroductionAge[days] = new HashSet<string>(items);
        }
        return State.IntroductionAge[days];
    }

    public async Task UpdateState(FilterState newState)
    {
        if (updateLock.CurrentCount == 0)
        {
            return;
        }
        try
        {
            await updateLock.WaitAsync();
            UpdateState(newState, State);
        }
        finally
        {
            updateLock.Release();
        }
    }

    private static void UpdateState(FilterState newState, FilterState local)
    {
        var properties = typeof(FilterState).GetProperties();
        foreach (var property in properties)
        {
            if (property.Name == nameof(FilterState.itemCategories))
            {
                continue;
            }
            if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                var localSet = (HashSet<string>)property.GetValue(local);
                var newSet = (HashSet<string>)property.GetValue(newState);
                localSet.Clear();
                foreach (var item in newSet)
                {
                    localSet.Add(item);
                }
            }
            else if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var localDict = (Dictionary<int, HashSet<string>>)property.GetValue(local);
                var newDict = (Dictionary<int, HashSet<string>>)property.GetValue(newState);
                foreach (var item in newDict)
                {
                    if (!localDict.ContainsKey(item.Key))
                    {
                        localDict[item.Key] = new HashSet<string>();
                    }
                    else
                    {
                        var localElem = localDict[item.Key];
                        localElem.Clear();
                        foreach (var itemValue in newDict[item.Key])
                        {
                            localElem.Add(itemValue);
                        }
                    }
                }
            }
            else
            {
                property.SetValue(local, property.GetValue(newState));
            }
        }
        // manually update item categories
        foreach (var item in newState.itemCategories)
        {
            if (!local.itemCategories.ContainsKey(item.Key))
            {
                local.itemCategories[item.Key] = new HashSet<string>();
            }
            local.itemCategories[item.Key].Clear();
            foreach (var itemValue in item.Value)
            {
                local.itemCategories[item.Key].Add(itemValue);
            }
        }
    }

    public record MayorResponse(
       [property: JsonProperty("success")] bool success,
       [property: JsonProperty("lastUpdated")] long lastUpdated,
       [property: JsonProperty("mayor")] Mayor mayor,
        [property: JsonProperty("current")] Current current
   );

    public record Current(
         [property: JsonProperty("year")] int year,
         [property: JsonProperty("candidates")] IReadOnlyList<Candidate> candidates
     );

    public record Candidate(
       [property: JsonProperty("key")] string key,
       [property: JsonProperty("name")] string name,
       [property: JsonProperty("perks")] IReadOnlyList<Perk> perks,
       [property: JsonProperty("votes")] int votes
   );

    public record Mayor(
        [property: JsonProperty("key")] string key,
        [property: JsonProperty("name")] string name,
        [property: JsonProperty("perks")] IReadOnlyList<Perk> perks,
        [property: JsonProperty("minister")] Minister minister
    );
    public record Minister(
        [property: JsonProperty("key")] string key,
        [property: JsonProperty("name")] string name,
        [property: JsonProperty("perk")] Perk perk
    );

    public record Perk(
        [property: JsonProperty("name")] string name,
        [property: JsonProperty("description")] string description,
        [property: JsonProperty("minister")] bool minister
    );
}
