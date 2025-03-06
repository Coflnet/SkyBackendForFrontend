
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared
{
    [DataContract]
    public class ListEntry
    {
        [DataMember(Name = "tag")]
        public string ItemTag;
        [DataMember(Name = "displayName")]
        public string DisplayName;
        [DataMember(Name = "filter")]
        public Dictionary<string, string> filter;
        [DataMember(Name = "tags")]
        public List<string> Tags;
        [DataMember(Name = "order")]
        public int Order;
        [DataMember(Name = "group")]
        public string Group;
        [DataMember(Name = "disabled")]
        public bool Disabled;

        private Func<FlipInstance, bool> filterCache;

        public bool MatchesSettings(FlipInstance flip, IPlayerInfo playerInfo)
        {
            if (filterCache == null)
                filterCache = GetExpression(playerInfo).Compile();
            return (ItemTag == null || ItemTag == flip.Auction.Tag) && filterCache(flip);
        }

        public Expression<Func<FlipInstance, bool>> GetExpression(IPlayerInfo playerInfo)
        {
            if (Disabled)
                return f => false;
            var filterCache = new FlipFilter(filter, playerInfo);
            //     Expression<Func<FlipInstance,bool>> normal = (flip) => (ItemTag == null || ItemTag == flip.Auction.Tag);
            return filterCache.GetExpression();
        }

        public override bool Equals(object obj)
        {
            return obj is ListEntry entry &&
                   ItemTag == entry.ItemTag &&
                   Disabled == entry.Disabled &&
                   DisplayName == entry.DisplayName &&
                   comparer.Equals(filter, entry.filter);
        }

        public static DictionaryComparer<string, string> comparer { get; } = new();

        public override int GetHashCode()
        {
            return HashCode.Combine(ItemTag, filter);
        }

        public ListEntry Clone()
        {
            return new ListEntry()
            {
                ItemTag = ItemTag,
                DisplayName = DisplayName,
                filter = filter == null ? null : new(filter),
                Tags = Tags == null ? null : new(Tags),
                Order = Order,
                Group = Group,
                Disabled = Disabled
            };
        }

        public class DictionaryComparer<TKey, TValue> :
                IEqualityComparer<Dictionary<TKey, TValue>>
        {
            private IEqualityComparer<TValue> valueComparer;
            public DictionaryComparer(IEqualityComparer<TValue> valueComparer = null)
            {
                this.valueComparer = valueComparer ?? EqualityComparer<TValue>.Default;
            }
            public bool Equals(Dictionary<TKey, TValue> x, Dictionary<TKey, TValue> y)
            {
                if (x == null && y == null)
                    return true;
                if (x == null || y == null)
                    return false;
                if (x.Count != y.Count)
                    return false;
                if (x.Keys.Except(y.Keys).Any())
                    return false;
                if (y.Keys.Except(x.Keys).Any())
                    return false;
                foreach (var pair in x)
                    if (!valueComparer.Equals(pair.Value, y[pair.Key]))
                        return false;
                return true;
            }

            public int GetHashCode(Dictionary<TKey, TValue> obj)
            {
                unchecked
                {
                    int hash = 17;
                    foreach (var pair in obj)
                    {
                        hash = hash * 23 + pair.Key.GetHashCode();
                        hash = hash * 23 + valueComparer.GetHashCode(pair.Value);
                    }
                    return hash;
                }
            }
        }
    }
}