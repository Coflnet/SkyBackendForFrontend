using System.Linq;
using Newtonsoft.Json;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using RestSharp;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System;

namespace Coflnet.Sky.Commands
{
    public class McAccountService
    {
        RestClient mcAccountClient = new RestClient(SimplerConfig.Config.Instance["MCCONNECT_BASE_URL"] ?? "http://" + SimplerConfig.Config.Instance["MCCONNECT_HOST"]);


        public async Task<Coflnet.Sky.McConnect.Models.MinecraftUuid> GetActiveAccount(int userId)
        {
            var mcRequest = new RestRequest("connect/user/{userId}")
                                .AddUrlSegment("userId", userId);
            McConnect.Models.User mcAccounts = await ExecuteUserRequest(mcRequest);
            return mcAccounts.Accounts.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault();
        }
        public async Task<IEnumerable<string>> GetAllAccounts(string userId, DateTime oldest = default)
        {
            if (userId == null)
                return new string[] { };
            List<McConnect.Models.MinecraftUuid> accounts = await AllAccountsInfo(userId);
            return accounts?.Where(a => a.Verified && a.LastRequestedAt > oldest).Select(a => a.AccountUuid).ToList() ?? new();
        }

        public async Task<List<McConnect.Models.MinecraftUuid>> AllAccountsInfo(string userId)
        {
            var mcRequest = new RestRequest("connect/user/{userId}")
                                            .AddUrlSegment("userId", userId);
            var mcAccounts = await ExecuteUserRequest(mcRequest);
            var accounts = mcAccounts?.Accounts;
            return accounts;
        }

        private async Task<McConnect.Models.User> ExecuteUserRequest(RestRequest mcRequest)
        {
            var mcResponse = await mcAccountClient.ExecuteAsync(mcRequest);
            if (mcResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                dev.Logger.Instance.Error("Error getting mc-accounts: " + mcResponse.Content);
                return null;
            }
            var mcAccounts = JsonConvert.DeserializeObject<Coflnet.Sky.McConnect.Models.User>(mcResponse.Content);
            return mcAccounts;
        }

        public async Task<ConnectionRequest> ConnectAccount(string userId, string uuid)
        {
            var response = (await mcAccountClient.ExecuteAsync(new RestRequest("connect/user/{userId}", Method.Post)
                                .AddUrlSegment("userId", userId).AddQueryParameter("mcUuid", uuid))).Content;
            try
            {
                return JsonConvert.DeserializeObject<ConnectionRequest>(response);
            }
            catch (System.Exception)
            {
                dev.Logger.Instance.Error("Parsing mc-verify response faield: " + response);
                throw;
            }
        }
        public async Task<Coflnet.Sky.McConnect.Models.User> GetUserId(string mcId)
        {
            return await ExecuteUserRequest(new RestRequest("connect/minecraft/{mcId}", Method.Get)
                                .AddUrlSegment("mcId", mcId));
        }

        [DataContract]
        public class ConnectionRequest
        {
            [DataMember(Name = "code")]
            public int Code { get; set; }
            [DataMember(Name = "isConnected")]
            public bool IsConnected { get; set; }
        }
    }
}