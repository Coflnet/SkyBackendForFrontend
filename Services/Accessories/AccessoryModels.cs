using System;
using System.Collections.Generic;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>Accessories a player owns, merged from the profile api and the opened accessory bag.</summary>
public class OwnedAccessories
{
    public HashSet<string> Tags { get; set; } = new();
    /// <summary>Accessories parsed from the opened accessory bag pages</summary>
    public List<BagAccessory> BagAccessories { get; set; } = new();
    /// <summary>Total pages of the accessory bag, 0 if unknown</summary>
    public int PageCount { get; set; }
    public List<int> SeenPages { get; set; } = new();
    public DateTime UpdatedAt { get; set; }

    /// <summary>True if the bag is known and some of its pages were never opened</summary>
    public bool BagPartiallySeen => PageCount > 0 && SeenPages.Count > 0 && SeenPages.Count < PageCount;
}

/// <summary>A not yet owned accessory with its price efficiency</summary>
public class CheapAccessory
{
    public string Tag { get; set; }
    public string ItemName { get; set; }
    public int PowerGain { get; set; }
    public long Price { get; set; }
    public long PricePerPower { get; set; }
}
