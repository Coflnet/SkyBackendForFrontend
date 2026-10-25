using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Commands.Shared;

public static class ActiveAuctionServiceExtensions
{
    /// <summary>
    /// Registers the in-memory active auction replica and explorer. Only call in services that serve it (sky-api),
    /// requires <c>AddCoflService</c> and <see cref="ItemFilterSearchService"/>.
    /// </summary>
    public static IServiceCollection AddActiveAuctions(this IServiceCollection services)
    {
        services.AddSingleton<ActiveAuctionSet>();
        services.AddHttpClient<IActiveIndexerClient, ActiveIndexerClient>();
        services.AddSingleton<ActiveAuctionDbLoader>(di => new ActiveAuctionDbLoader(null,
            di.GetService<Microsoft.Extensions.Logging.ILogger<ActiveAuctionDbLoader>>()));
        services.AddSingleton<ActiveAuctionReplica>();
        services.AddHostedService(di => di.GetRequiredService<ActiveAuctionReplica>());
        services.AddSingleton<ArmorSetResolver>();
        services.AddSingleton<ActiveSearchQueryResolver>();
        services.AddSingleton<ActiveAuctionQueryService>();
        return services;
    }
}
