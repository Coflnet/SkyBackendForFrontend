using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands
{
    public interface IFlipConnection
    {
        /// <summary>
        /// Tries to send a flip, returns false if the connection can no longer send flips
        /// </summary>
        /// <param name="flip"></param>
        /// <returns></returns>
        Task<bool> SendFlip(FlipInstance flip);
        Task SendBatch(IEnumerable<LowPricedAuction> flips);
        Task<bool> SendFlip(LowPricedAuction flip);
        Task<bool> SendSold(string uuid);
        FlipSettings Settings { get; }
        AccountInfo AccountInfo { get; }
        Task<AccountTier> UserAccountTier();
        long Id { get; }
        string UserId { get; }
        string GameServer { get; }
        /// <summary>
        /// Logs information to the connection
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="level"></param>
        void Log(string message, LogLevel level = LogLevel.Information);
    }
}
