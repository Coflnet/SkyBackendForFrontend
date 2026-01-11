using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Items.Client.Api;

namespace Coflnet.Sky.Commands.Shared;

public class BitService
{
    private readonly ISniperClient sniperClient;
    private readonly IBazaarApi bazaarApi;
    private readonly IItemsApi itemsApi;
    private readonly PlayerState.Client.Api.IBitApi bitApi;

    public BitService(ISniperClient sniperClient, IBazaarApi bazaarApi, IItemsApi itemsApi, PlayerState.Client.Api.IBitApi bitApi)
    {
        this.sniperClient = sniperClient;
        this.bazaarApi = bazaarApi;
        this.itemsApi = itemsApi;
        this.bitApi = bitApi;
    }
    public async Task<List<Option>> GetOptions()
    {
        var bitsApiTask = bitApi.BitMappingsGetAsync();
        var itemNamesTask = itemsApi.ItemNamesGetAsync();
        var cleanPrices = sniperClient.GetCleanPrices();
        var bazaarPrices = bazaarApi.GetAllPricesAsync();

        var bitsMap = await bitsApiTask;
        var itemNames = (await itemNamesTask).ToDictionary(i => i.Tag, i => i.Name);
        var cleanPricesMap = await cleanPrices;
        foreach (var item in await bazaarPrices)
        {
            cleanPricesMap[item.ProductId] = (long)item.BuyPrice;
        }
        return bitsMap.Select(bit =>
        {
            itemNames.TryGetValue(bit.ItemTag, out var name);
            cleanPricesMap.TryGetValue(bit.ItemTag, out var itemValue);
            return new Option(
                name ?? bit.ItemTag,
                bit.ItemTag,
                itemValue / bit.BitValue,
                bit.BitValue
            );
        }).OrderByDescending(o => o.CoinsPerBit).ToList();
    }
    
    public record Option(string Name, string Tag, float CoinsPerBit, float TotalBits);
}
