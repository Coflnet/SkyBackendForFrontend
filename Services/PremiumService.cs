using System;
using Google.Apis.Auth;
using Coflnet.Sky.Core;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.Commands.Shared
{
    public class PremiumService
    {
        static string premiumPlanName;
        static string testpremiumPlanName;
        static string premiumPlusSlug;
        static string starterPremiumSlug;
        static string preApiSlug;

        private UserApi userApi;

        public PremiumService(UserApi userApi, IConfiguration config)
        {
            this.userApi = userApi;
            premiumPlanName = GetRequired(config, "PRODUCTS:PREMIUM");
            testpremiumPlanName = GetRequired(config, "PRODUCTS:TEST_PREMIUM");
            premiumPlusSlug = GetRequired(config, "PRODUCTS:PREMIUM_PLUS");
            starterPremiumSlug = GetRequired(config, "PRODUCTS:STARTER_PREMIUM");
            preApiSlug = GetRequired(config, "PRODUCTS:PRE_API");

            static string GetRequired(IConfiguration config, string key)
            {
                return config[key] ?? throw new Exception($"Required setting {key} isn't configured");
            }
        }

        public GoogleJsonWebSignature.Payload ValidateToken(string token)
        {
            try
            {
                var client = GoogleJsonWebSignature.ValidateAsync(token);
                client.Wait();
                var tokenData = client.Result;
                Console.WriteLine("google user: " + tokenData.Name);
                return tokenData;
            }
            catch (Exception e)
            {
                throw new CoflnetException("invalid_token", $"{e.InnerException.Message}");
            }
        }

        public async Task<DateTime> ExpiresWhen(int userId)
        {
            return await ExpiresWhen(userId.ToString());
        }

        public async Task<(AccountTier?, DateTime)> GetCurrentTier(string userId)
        {
            if(string.IsNullOrEmpty(userId))
                return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromMinutes(3));
            try
            {
                if (GoogleUser.EveryoneIsPremium)
                    return (AccountTier.PREMIUM_PLUS, DateTime.UtcNow + TimeSpan.FromDays(30));
                var owns = await userApi.UserUserIdOwnsUntilPostAsync(userId, new() { premiumPlanName, premiumPlusSlug, starterPremiumSlug, preApiSlug, "test-premium" });
                if (owns.TryGetValue(preApiSlug, out DateTime end) && end > DateTime.UtcNow)
                    return (AccountTier.SUPER_PREMIUM, end);
                if (owns.TryGetValue(premiumPlusSlug, out end) && end > DateTime.UtcNow)
                    return (AccountTier.PREMIUM_PLUS, end);
                if ((owns.TryGetValue(premiumPlanName, out end) || owns.TryGetValue("test-premium", out end)) && end > DateTime.UtcNow)
                    return (AccountTier.PREMIUM, end);
                if (owns.TryGetValue(starterPremiumSlug, out end) && end > DateTime.UtcNow)
                    return (AccountTier.STARTER_PREMIUM, end);
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "retrieving premium status for " + userId);
                return (null, DateTime.UtcNow + TimeSpan.FromMinutes(3));
            }
            return (AccountTier.NONE, DateTime.UtcNow + TimeSpan.FromHours(3));
        }

        public async Task<DateTime> ExpiresWhen(string userId)
        {
            if (GoogleUser.EveryoneIsPremium)
                return DateTime.UtcNow + TimeSpan.FromDays(30);
            var until = await userApi.UserUserIdOwnsLongestPostAsync(userId, new() { premiumPlanName, testpremiumPlanName });
            return until;
        }
        public async Task<bool> HasPremium(int userId)
        {
            return (await ExpiresWhen(userId)) > DateTime.UtcNow;
        }
    }
}