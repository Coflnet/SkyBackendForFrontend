using System.Collections.Generic;
using Coflnet.Sky.Referral.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class DiHandlerTests
{
    [Test]
    public void FilterStateIsSharedBetweenBuiltProviders()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ITEMS_BASE_URL"] = "http://items.test",
                ["MAYOR_BASE_URL"] = "http://mayor.test"
            })
            .Build());
        services.AddLogging();
        services.AddCoflService();
        using var earlyProvider = services.BuildServiceProvider();
        using var hostProvider = services.BuildServiceProvider();

        Assert.That(
            earlyProvider.GetRequiredService<FilterStateService>().State,
            Is.SameAs(hostProvider.GetRequiredService<FilterStateService>().State));
    }

    [Test]
    public void ReferralClientReceivesConfiguredMutationToken()
    {
        const string token = "referral-mutation-token-32-characters-minimum";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["REFERRAL_BASE_URL"] = "http://referral.test",
                ["REFERRAL_MUTATION_TOKEN"] = token
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCoflService();
        using var provider = services.BuildServiceProvider();

        var referral = (ReferralApi)provider.GetRequiredService<IReferralApi>();

        Assert.That(
            referral.Configuration.DefaultHeaders["X-Referral-Mutation-Token"],
            Is.EqualTo(token));
    }
}
