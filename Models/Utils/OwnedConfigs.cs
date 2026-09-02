using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.Shared;

public class OwnedConfigs
{
    public List<OwnedConfig> Configs { get; set; } = new();
    public HashSet<long> RevertedPurchaseIds { get; set; } = new();
    public class OwnedConfig
    {
        public string Name { get; set; }
        public int Version { get; set; }
        public string ChangeNotes { get; set; }
        public string OwnerId { get; set; }
        public string OwnerName { get; set; }
        public int PricePaid { get; set; }
        public long PurchaseTransactionId { get; set; }
        public Guid? RewardPendingId { get; set; }
        public long CreatorFeeEurCents { get; set; }
        public DateTime BoughtAt { get; set; } = DateTime.UtcNow;
        public DateTime? AccessUntilUtc { get; set; }
        public bool CreatorGift { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
    }
}
