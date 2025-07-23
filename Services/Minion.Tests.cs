using System.Linq;
using System.Threading.Tasks;
using System;
using NUnit.Framework;
using Coflnet.Sky.Items.Client.Api;
using FluentAssertions;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.Shared;

public class MinionTests
{
    public class MinionRate
    {
        public string Name { get; set; }
        public double[] Seconsds { get; set; }
        public int[] Capacity { get; set; }
    }
    //[Test] //not run every time, only for transforming item data
    public async Task TransformMinions()
    {
        var itemsApi = new ItemsApi("http://localhost:5014");
        var timings = JsonConvert.DeserializeObject<MinionRate[]>(System.IO.File.ReadAllText("Services/timings.json"));
        var s = new MinionService();
        foreach (var minion in s.MinionData.Values)
        {
            foreach (var item in minion.Products)
            {
                if(item.Tag != null)
                    continue; // Skip if tag is already set
                var itemData = await itemsApi.ItemsSearchTermGetAsync(item.ItemName);
                Console.WriteLine($"{item.ItemName} -> {itemData[0].Tag}");
                item.Tag = itemData[0].Tag;
            }
            foreach (var item in minion.Upgrade.SelectMany(x => x))
            {
                if(item.Tag != null)
                    continue; // Skip if tag is already set
                var itemData = await itemsApi.ItemsSearchTermGetAsync(item.Name);
                if (itemData.Count == 0)
                {
                    Console.WriteLine($"No item found for {item.Name}");
                    continue;
                }
                Console.WriteLine($"{item.Name} -> {itemData[0].Tag}");
                item.Tag = itemData[0].Tag;
            }
        }
        foreach (var item in timings)
        {
            if(!s.MinionData.ContainsKey(item.Name))
            {
                Console.WriteLine($"Minion {item.Name} not found in data");
                continue;
            }
            s.MinionData[item.Name].TierDelay.Clear();
            s.MinionData[item.Name].TierDelay.AddRange(item.Seconsds);
            s.MinionData[item.Name].Storage.Clear();
            s.MinionData[item.Name].Storage.AddRange(item.Capacity);
        }
        var json = System.Text.Json.JsonSerializer.Serialize(s.MinionData.Values, new System.Text.Json.JsonSerializerOptions() { WriteIndented = true });
        System.IO.File.WriteAllText("Services/minion_data2.json", json);
    }

    [Test]
    public void MinionCalculation()
    {
        var s = new MinionService();
        var itemPrices = new System.Collections.Generic.Dictionary<string, double>()
        {
            { "REVENANT_FLESH", 160 },
            { "ENCHANTED_DIAMOND", 1200 },
            { "DIAMOND", 6},
            { "ENCHANTED_ROTTEN_FLESH", 700 },
            {"ROTTEN_FLESH", 1.8},
            {"REVENANT_VISCERA", 60_000}
        };
        var effects = s.GetCurrentEffects(itemPrices);
        Assert.That(effects.Count, Is.GreaterThan(0));
        effects.First().name.Should().Be("Revenant Minion");
        effects.First().profitPerDay.Should().BeGreaterThan(0);
        foreach (var effect in effects)
        {
            Console.WriteLine($"{effect.name}: Profit per day: {effect.profitPerDay}, Craft cost: {effect.craftCostEst}, NBC Sell per day: {effect.nbcSellPerDay}");
        }
    }
}
