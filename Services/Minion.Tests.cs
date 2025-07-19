using System.Linq;
using System.Threading.Tasks;
using System;
using NUnit.Framework;
using Coflnet.Sky.Items.Client.Api;

namespace Coflnet.Sky.Commands.Shared;

public class MinionTests
{
    //[Test] not run every time, only for transforming item data
    public async Task TransformMinions()
    {
        var itemsApi = new ItemsApi("http://localhost:5014");
        var s = new MinionService();
        foreach (var minion in s.MinionData.Values)
        {
            foreach (var item in minion.Products)
            {
                var itemData = await itemsApi.ItemsSearchTermGetAsync(item.ItemName);
                Console.WriteLine($"{item.ItemName} -> {itemData[0].Tag}");
                item.Tag = itemData[0].Tag;
            }
            foreach (var item in minion.Upgrade.SelectMany(x => x))
            {
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
        var json = System.Text.Json.JsonSerializer.Serialize(s.MinionData.Values, new System.Text.Json.JsonSerializerOptions() { WriteIndented = true });
        System.IO.File.WriteAllText("Services/minion_data2.json", json);
    }
}
