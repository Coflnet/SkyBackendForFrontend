using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Payments.Client.Api;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Sky.PlayerName.Client.Api;

namespace Coflnet.Sky.Commands;

public interface ILeaderboardService
{
    Task<IEnumerable<LeaderboardService.LeaderboardEntry>> GetTopFlippers(global::System.String boardName = "sky-flipers", DateTime weekStartDate = default, global::System.Int32 page = 0, global::System.Int32 count = 10);
    Task HideAccount(global::System.String userId, global::System.String accountId, DateTime expiresAt);
}

public class LeaderboardService : ILeaderboardService
{
    private IScoresApi scoresApi;
    private SettingsService settingsService;
    private IPlayerNameApi playerNameApi;
    private IUserApi userApi;

    public LeaderboardService(IScoresApi scoresApi, SettingsService settingsService, IPlayerNameApi playerNameApi, IUserApi userApi)
    {
        this.scoresApi = scoresApi;
        this.settingsService = settingsService;
        this.playerNameApi = playerNameApi;
        this.userApi = userApi;
    }

    public async Task<IEnumerable<LeaderboardEntry>> GetTopFlippers(string boardName = "sky-flipers", DateTime weekStartDate = default, int page = 0, int count = 10)
    {
        if (weekStartDate == default)
            weekStartDate = DateTime.UtcNow;
        var boardSlug = $"{boardName}-{weekStartDate.RoundDown(TimeSpan.FromDays(7)):yyyy-MM-dd}";
        var excludedTask = GetExcluded();
        var leaderboardData = await scoresApi.ScoresLeaderboardSlugGetAsync(boardSlug, page * count, count);
        var names = await playerNameApi.PlayerNameNamesBatchPostAsync(leaderboardData.Select(d => d.UserId).ToList());
        var excluded = await excludedTask;
        var entries = leaderboardData.Select(async entry =>
        {
            if (excluded.Users.TryGetValue(entry.UserId, out var hideInfo))
            {
                if (hideInfo.expiresAt < DateTime.UtcNow)
                {
                    var currentExpiry = await Expiry(hideInfo.userId);
                    if (currentExpiry < DateTime.UtcNow)
                    {
                        // remove from excluded
                        excluded.Users.Remove(entry.UserId);
                    }
                    else
                    {
                        hideInfo = (hideInfo.userId, currentExpiry);
                        excluded.Users[entry.UserId] = hideInfo;
                    }
                    await settingsService.UpdateSetting("global", "hidden_flipper_accounts", excluded);
                }
                if (hideInfo.expiresAt > DateTime.UtcNow)
                {
                    return new LeaderboardEntry()
                    {
                        PlayerId = Guid.Empty.ToString("N"),
                        PlayerName = "anonymous",
                        Score = entry.Score
                    };
                }
            }
            var name = names.GetValueOrDefault(entry.UserId, "anonymous");
            return new LeaderboardEntry()
            {
                PlayerId = entry.UserId,
                PlayerName = name,
                Score = entry.Score
            };
        });
        return await Task.WhenAll(entries);
    }

    public async Task HideAccount(string userId, string accountId, DateTime expiresAt)
    {
        HiddenAccountsSetting settings = await GetExcluded();
        DateTime premiumPlusExpires = await Expiry(userId);
        if (premiumPlusExpires > DateTime.Now)
        {
            settings.Users[accountId] = (userId, expiresAt);
        }
        await settingsService.UpdateSetting("global", "hidden_flipper_accounts", settings);
    }

    private async Task<DateTime> Expiry(string userId)
    {
        return await this.userApi.UserUserIdOwnsLongestPostAsync(userId, ["premium_plus"]);
    }

    private async Task<HiddenAccountsSetting> GetExcluded()
    {
        return await settingsService.GetCurrentValue<HiddenAccountsSetting>("global", "hidden_flipper_accounts", () => new HiddenAccountsSetting());
    }

    public class HiddenAccountsSetting
    {
        public Dictionary<string, (string userId, DateTime expiresAt)> Users = new();
    }

    public class LeaderboardEntry
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public long Score { get; set; }
    }
}
