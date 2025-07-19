using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.Shared;

public class MinionService
{
    public Dictionary<string, Minion> MinionData { get; set; } = new Dictionary<string, Minion>();
    public MinionService()
    {
        var json = File.ReadAllText("Services/minion_data.json");
        var minions = JsonConvert.DeserializeObject<List<Minion>>(json);
        if (minions == null)
            throw new InvalidDataException("Minion data is null, check the JSON file");
        foreach (var minion in minions)
        {
            MinionData[minion.Name] = minion;
        }
    }

    public List<MinionEffect> GetCurrentEffects(Dictionary<string, double> itemPrices)
    {
        var effects = new List<MinionEffect>();
        foreach (var minion in MinionData.Values)
        {
            double profitPerDay = 0;
            double nbcSellPerDay = 0;
            double profitPerAction = 0;
            double npcProfitPerAction = 0;

            foreach (var product in minion.Products)
            {
                if (product.Tag == null)
                    continue; // Skip if tag is not set
                if (itemPrices.TryGetValue(product.Tag, out var price))
                {
                    profitPerAction += price * product.PerTime;
                    npcProfitPerAction += product.NpcPrice * product.PerTime;
                }
            }
            var costForTopTier = minion.Upgrade.Sum(x => x.Sum(y => y?.Tag != null && itemPrices.TryGetValue(y.Tag, out var price) ? price * y.Quanity : 0));
            profitPerDay = profitPerAction * 24 * 3600 / minion.TierDelay.Last();
            nbcSellPerDay = npcProfitPerAction * 24 * 3600 / minion.TierDelay.Last();

            effects.Add(new MinionEffect(minion.Name, profitPerDay, costForTopTier, nbcSellPerDay, minion));
        }
        return effects;
    }

    public record MinionEffect(string name,
        double profitPerDay,
        double craftCostEst,
        double nbcSellPerDay,
        Minion minionData);



    public record Product(
    [property: JsonProperty("itemName")]
        [property: JsonPropertyName("itemName")] string ItemName,
    [property: JsonProperty("perTime")]
        [property: JsonPropertyName("perTime")] double PerTime,
    [property: JsonProperty("npcPrice")]
        [property: JsonPropertyName("npcPrice")] double NpcPrice,
    [property: JsonProperty("exp")]
        [property: JsonPropertyName("exp")] object Exp,
    [property: JsonProperty("expType")]
        [property: JsonPropertyName("expType")] object ExpType
)
    {
        [JsonPropertyName("tag")]
        public string Tag { get; internal set; }
    }


    public record Minion(
        [property: JsonProperty("name")]
        [property: JsonPropertyName("name")] string Name,
        [property: JsonProperty("tierDelay")]
        [property: JsonPropertyName("tierDelay")] IReadOnlyList<double> TierDelay,
        [property: JsonProperty("storage")]
        [property: JsonPropertyName("storage")] IReadOnlyList<int> Storage,
        [property: JsonProperty("type")]
        [property: JsonPropertyName("type")] string Type,
        [property: JsonProperty("upgrade")]
        [property: JsonPropertyName("upgrade")] IReadOnlyList<List<Cost>> Upgrade,
        [property: JsonProperty("products")]
        [property: JsonPropertyName("products")] IReadOnlyList<Product> Products
    );

    public record Cost(
        [property: JsonProperty("itemName")]
        [property: JsonPropertyName("itemName")] string Name,
        [property: JsonProperty("quantity")]
        [property: JsonPropertyName("quantity")] double Quanity
    )
    {
        [JsonPropertyName("tag")]
        public string Tag { get; internal set; }
    }

}