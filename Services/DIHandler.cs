using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Coflnet.Payments.Client.Api;
using System;
using Coflnet.Payments.Client.Client;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Referral.Client.Api;
using Coflnet.Sky.Sniper.Client.Api;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.McConnect.Api;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Threading;
using Coflnet.Sky.FlipTracker.Client.Api;
using Coflnet.Sky.Mayor.Client.Api;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Sky.Settings.Client.Api;
using Coflnet.Sky.Filter;
using Coflnet.Sky.EventBroker.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Core.Services;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared
{
    public static class DiHandler
    {
        private static System.IServiceProvider _serviceProvider;
        public static System.IServiceProvider ServiceProvider
        {
            get
            {
                if (_serviceProvider == null)
                    try
                    {
                        _serviceProvider = _servics.BuildServiceProvider();
                    }
                    catch (System.Exception)
                    {
                        Console.WriteLine("Failed to build service provider\nCheck that you called AddCoflService on the service collection in Startup.cs");
                        throw;
                    }
                return _serviceProvider;
            }
        }

        private static IServiceCollection _servics;
        public static void AddCoflService(this IServiceCollection services)
        {
            services.AddSingleton<PlayerName.Client.Api.IPlayerNameApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new PlayerName.Client.Api.PlayerNameApi(config["PLAYERNAME_BASE_URL"] ?? "http://" + config["PLAYERNAME_HOST"]);
            });
            services.AddSingleton<Bazaar.Client.Api.BazaarApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                var url = config["BAZAAR_BASE_URL"];
                if (url == null)
                    throw new Exception("config option BAZAAR_BASE_URL is not set");
                return new Bazaar.Client.Api.BazaarApi(url);
            });
            services.AddSingleton<Bazaar.Client.Api.IBazaarApi, Bazaar.Client.Api.BazaarApi>(di => di.GetRequiredService<Bazaar.Client.Api.BazaarApi>());
            services.AddSingleton<SettingsService>();
            services.AddSingleton<UpgradePriceService>();
            //services.AddHostedService<UpgradePriceService>(di => di.GetRequiredService<UpgradePriceService>());
            services.AddSingleton<FlipTrackingService>();
            services.AddSingleton<IFlipTrackingService>(di => di.GetRequiredService<FlipTrackingService>());
            services.AddPaymentSingleton<ProductsApi>(url => new ProductsApi(url));
            services.AddPaymentSingleton<UserApi>(url => new UserApi(url));
            services.AddPaymentSingleton<TopUpApi>(url => new TopUpApi(url));
            services.AddPaymentSingleton<IProductsApi>(url => new ProductsApi(url));
            services.AddPaymentSingleton<IUserApi>(url => new UserApi(url));
            services.AddPaymentSingleton<ITopUpApi>(url => new TopUpApi(url));
            services.AddPaymentSingleton<IGooglePayApi>(url => new GooglePayApi(url));
            services.AddPaymentSingleton<ICreatorCodeApi>(url => new CreatorCodeApi(url));
            services.AddPaymentSingleton<ITransactionApi>(url => new TransactionApi(url));
            services.AddPaymentSingleton<ILicenseApi>(url => new LicenseApi(url));
            services.AddPaymentSingleton<ISubscriptionApi>(url => new SubscriptionApi(url));
            services.AddSingleton<TokenService>();
            services.AddSingleton<IItemsApi>(di=>
            {
                var config = di.GetRequiredService<IConfiguration>();
                return new ItemsApi(config["ITEMS_BASE_URL"]);
            });
            services.AddSingleton<IReferralApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new ReferralApi(config["REFERRAL_BASE_URL"]);
            });
            services.AddSingleton<ISniperApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new SniperApi(config["SNIPER_BASE_URL"]);
            });
            services.AddSingleton<Sniper.Client.Api.IAuctionApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new Sniper.Client.Api.AuctionApi(config["SNIPER_BASE_URL"]);
            });
            services.AddSingleton<IAttributeApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new AttributeApi(config["SNIPER_BASE_URL"]);
            });
            static string GetCraftsBaseUrl(IServiceProvider provider) =>
                provider.GetRequiredService<IConfiguration>()["CRAFTS_BASE_URL"];

            services.AddSingleton<ICraftsApi>(context => new CraftsApi(GetCraftsBaseUrl(context)));
            services.AddSingleton<INpcApi>(context => new NpcApi(GetCraftsBaseUrl(context)));
            services.AddSingleton<IDropApi>(context => new DropApi(GetCraftsBaseUrl(context)));
            services.AddSingleton<IKatApi>(context => new KatApi(GetCraftsBaseUrl(context)));
            services.AddSingleton<IForgeApi>(context => new ForgeApi(GetCraftsBaseUrl(context)));
            services.AddSingleton<Api.Client.Api.IPricesApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new Api.Client.Api.PricesApi(config["API_BASE_URL"]);
            });
            services.AddSingleton<Api.Client.Api.ISearchApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new Api.Client.Api.SearchApi(config["API_BASE_URL"]);
            });
            services.AddSingleton<Api.Client.Api.IPlayerApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new Api.Client.Api.PlayerApi(config["API_BASE_URL"]);
            });
            services.AddSingleton<Api.Client.Api.IAuctionsApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new Api.Client.Api.AuctionsApi(config["API_BASE_URL"]);
            });
            services.AddSingleton<Api.Client.Api.IModApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new Api.Client.Api.ModApi(config["API_BASE_URL"]);
            });
            services.AddSingleton<IAnalyseApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new AnalyseApi(config["FLIPTRACKER_BASE_URL"]);
            });
            services.AddSingleton<ITrackerApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new TrackerApi(config["FLIPTRACKER_BASE_URL"]);
            });
            services.AddSingleton<IMayorApiApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new MayorApiApi(config["MAYOR_BASE_URL"]);
            });
            services.AddSingleton<IScoresApi, ScoresApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new ScoresApi(config["LEADERBOARD_BASE_URL"]);
            });
            services.AddSingleton<ISettingsApi, SettingsApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new SettingsApi(config["SETTINGS_BASE_URL"]);
            });
            services.AddSingleton<FilterEngine>();
            services.AddSingleton<IConnectApi, ConnectApi>(
                sp => new ConnectApi(sp.GetRequiredService<IConfiguration>()["MCCONNECT_BASE_URL"]));
            services.AddSingleton<PlayerState.Client.Api.IPlayerStateApi, PlayerState.Client.Api.PlayerStateApi>(
                sp => new PlayerState.Client.Api.PlayerStateApi(sp.GetRequiredService<IConfiguration>()["PLAYERSTATE_BASE_URL"]));
            services.AddSingleton<PlayerState.Client.Api.ITransactionApi, PlayerState.Client.Api.TransactionApi>(
                sp => new PlayerState.Client.Api.TransactionApi(sp.GetRequiredService<IConfiguration>()["PLAYERSTATE_BASE_URL"]));

            services.AddSingleton<PremiumService>();
            services.AddSingleton<ISniperClient, SniperClient>();
            services.AddSingleton<EventBrokerClient>();
            services.AddSingleton<MinionService>();
            services.AddSingleton<ComposterService>();
            services.AddSingleton<ISubscriptionsApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new SubscriptionsApi(config["EVENTS_BASE_URL"]);
            });
            services.AddSingleton<ISubscriptionsApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new SubscriptionsApi(config["EVENTS_BASE_URL"]);
            });
            services.AddSingleton<ITargetsApi>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                return new TargetsApi(config["EVENTS_BASE_URL"]);
            });
            services.AddSingleton<PlayerName.PlayerNameService>();
            services.AddSingleton<IdConverter>();
            services.AddSingleton<AuctionService>();
            services.AddSingleton<FlipperService>();
            services.AddSingleton<Kafka.KafkaCreator>();
            services.AddSingleton<IStateUpdateService, StateUpdateService>();
            services.AddSingleton<McAccountService>();
            services.AddSingleton<MuseumService>();
            services.AddSingleton<SearchService>();
            services.AddHostedService<ServicePorter>();
            services.AddSingleton<NBT>();
            services.AddSingleton<INBT>(di=> di.GetRequiredService<NBT>());
            services.AddSingleton<ItemDetails>();
            services.AddHostedService<FilterLoader>();
            services.AddTransient<HypixelContext>(s => new HypixelContext());
            services.AddSingleton<FilterStateService>();
            services.AddSingleton<HypixelItemService>();
            services.AddSingleton<IHypixelItemStore>(di => di.GetRequiredService<HypixelItemService>());
            services.AddHttpClient();
            services.AddSingleton<IProfileClient, ProfileClient>();
            services.AddSingleton<ForgeFlipService>();
            services.AddSingleton<IBazaarFlipperApi, BazaarFlipperApi>(s =>
                new BazaarFlipperApi(s.GetRequiredService<IConfiguration>()["BAZAARFLIPPER_BASE_URL"]));
            services.AddSingleton<Auctions.Client.Api.IAuctionApi>(s =>
                new Auctions.Client.Api.AuctionApi(s.GetRequiredService<IConfiguration>()["AUCTIONS_BASE_URL"]));

            _servics = services;
        }

        public static void AddTestServices()
        {
            if (_servics == null)
                _servics = new ServiceCollection();
            _servics.AddLogging(b => b.AddConsole());
            _servics.AddSingleton(new System.Diagnostics.ActivitySource("Test"));
            _serviceProvider = null;
        }

        public class ServicePorter : BackgroundService
        {
            public ServicePorter(IServiceProvider services)
            {
                _serviceProvider = services;
            }
            protected override Task ExecuteAsync(CancellationToken stoppingToken)
            {
                return Task.CompletedTask;
            }
        }

        public static void OverrideService<T, TImpl>(TImpl service) where T : class where TImpl : T
        {
            if (_servics == null)
                _servics = new ServiceCollection();
            _servics.AddSingleton<T>(service);
            _serviceProvider = null;
        }

        public static void ResetProvider()
        {
            _serviceProvider = null;
        }

        public static void AddPaymentSingleton<T>(this IServiceCollection services, Func<string, T> creator) where T : class, IApiAccessor
        {
            services.AddSingleton<T>(context =>
            {
                var config = context.GetRequiredService<IConfiguration>();
                var url = config["PAYMENTS_BASE_URL"];
                if (url == null)
                    url = "http://" + config["PAYMENTS_HOST"];
                return creator(url);
            });
        }

        public static T GetService<T>()
        {
            return ServiceProvider.GetRequiredService<T>();
        }
    }
}

namespace Coflnet.Sky.Core
{
    public static class DiExtentions
    {
        public static T GetService<T>(this MessageData di)
        {
            return DiHandler.ServiceProvider.GetService<T>();
        }
    }
}