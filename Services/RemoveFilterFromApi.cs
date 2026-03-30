using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi;
using System;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;
public class RemoveFilterFromApi : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Paths.Remove("/api/Filter");
    }
}

public class RemoveAuctionFromApi : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        context.SchemaRepository.Schemas.Remove("ApiSaveAuction");
        context.SchemaRepository.Schemas.Remove("FilterQuery");
        context.SchemaRepository.Schemas.Remove(typeof(SaveAuction).Name);
        context.SchemaRepository.Schemas.Remove(typeof(SaveBids).Name);
        context.SchemaRepository.Schemas.Remove(typeof(Category).Name);
        context.SchemaRepository.Schemas.Remove(typeof(Enchantment).Name);
        context.SchemaRepository.Schemas.Remove(typeof(Enchantment.EnchantmentType).Name);
        context.SchemaRepository.Schemas.Remove(typeof(NBTLookup).Name);
        context.SchemaRepository.Schemas.Remove(typeof(NbtData).Name);
        context.SchemaRepository.Schemas.Remove(typeof(ItemReferences.Reforge).Name);
        context.SchemaRepository.Schemas.Remove(typeof(Tier).Name);
    }
}