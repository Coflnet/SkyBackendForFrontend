using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Coflnet.Sky.Commands.Shared
{
    /// <summary>
    /// Picks which ranked bazaar plan candidates free users get to see.
    /// This is the single place the free-tier policy lives so it can be tuned later.
    /// </summary>
    public static class FreeTierPlanSampler
    {
        public const int SampleCount = 3;
        /// <summary>1-based first rank of the sampling window</summary>
        public const int RankWindowStart = 4;
        /// <summary>1-based last rank of the sampling window</summary>
        public const int RankWindowEnd = 10;

        /// <summary>
        /// 0-based indices of up to <see cref="SampleCount"/> candidates from 1-based ranks 4..10 (clamped to the
        /// candidate count), deterministic per user and UTC day, without replacement, ascending.
        /// </summary>
        public static IReadOnlyList<int> SampleIndices(int candidateCount, string userId, DateTime utcDay)
        {
            if (candidateCount < RankWindowStart)
                return Array.Empty<int>();
            var last = Math.Min(candidateCount, RankWindowEnd);
            var pool = Enumerable.Range(RankWindowStart - 1, last - RankWindowStart + 1).ToList();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId + utcDay.ToString("yyyy-MM-dd")));
            var picks = new List<int>();
            for (int i = 0; i < SampleCount && pool.Count > 0; i++)
            {
                var r = BitConverter.ToUInt32(hash, (i * 4) % (hash.Length - 3));
                var index = (int)(r % (uint)pool.Count);
                picks.Add(pool[index]);
                pool.RemoveAt(index);
            }
            picks.Sort();
            return picks;
        }
    }
}
