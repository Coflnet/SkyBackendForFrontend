using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Coflnet.Sky.Commands.Shared.Goals;

/// <summary>Payload of the mod's infoDisplay message (camelCase json).</summary>
[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InfoDisplayDto
{
    public int Id { get; set; } = 3;
    public string Title { get; set; }
    public List<ChatPartDto> Lines { get; set; } = new();
    public int Ttl { get; set; } = 600;
    public bool Clear { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class ChatPartDto
{
    public string Text { get; set; }
    public string Hover { get; set; }
    public string OnClick { get; set; }
}

public static class GoalHud
{
    public static InfoDisplayDto BuildDisplay(GoalList list, IReadOnlyDictionary<string, GoalProgress> progressByGoalId,
        bool hasCookie, Func<string, string> nameLookup, Func<string, string> searchNameLookup,
        IReadOnlyDictionary<string, string> lbinUuidByTag)
    {
        var goals = list?.Goals ?? new();
        if (goals.Count == 0)
            return new InfoDisplayDto { Clear = true };
        var focus = goals.FirstOrDefault(g => g.Id == list.FocusId) ?? goals[0];
        var dto = new InfoDisplayDto();
        var focusProgress = Get(progressByGoalId, focus.Id);
        dto.Title = $"§6§lGoal: §f{focus.Title} §7{focusProgress?.Done ?? 0}/{focusProgress?.Total ?? 0}";
        if (focusProgress != null)
            foreach (var step in focusProgress.Actionable)
                dto.Lines.Add(StepLine(step, hasCookie, nameLookup, searchNameLookup, lbinUuidByTag));
        foreach (var other in goals.Where(g => g != focus))
        {
            var p = Get(progressByGoalId, other.Id);
            dto.Lines.Add(new ChatPartDto
            {
                Text = $"§7▸ {other.Title} {p?.Done ?? 0}/{p?.Total ?? 0}",
                Hover = "Click to focus this goal",
                OnClick = $"/cofl goal focus {other.Id}"
            });
        }
        return dto;
    }

    private static GoalProgress Get(IReadOnlyDictionary<string, GoalProgress> map, string id)
        => map != null && map.TryGetValue(id, out var p) ? p : null;

    private static ChatPartDto StepLine(GoalStep step, bool hasCookie, Func<string, string> nameLookup,
        Func<string, string> searchNameLookup, IReadOnlyDictionary<string, string> lbin)
    {
        var name = nameLookup(step.ItemTag) ?? step.ItemTag;
        var qty = step.Quantity;
        switch (step.Kind)
        {
            case StepKind.BuyOrder:
            case StepKind.InstaBuy:
                var search = searchNameLookup(step.ItemTag) ?? name;
                return new ChatPartDto
                {
                    Text = $"§c{qty}x {name}",
                    Hover = hasCookie ? $"Click to place a bazaar order for {qty}x {name}" : "Open the bazaar first, then click",
                    OnClick = hasCookie ? $"/cofl bzorder {qty} {search}" : $"/cofl bzorder search {qty} {search}"
                };
            case StepKind.Auction:
                var hasLbin = lbin != null && lbin.TryGetValue(step.ItemTag, out var uuid) && !string.IsNullOrEmpty(uuid);
                return new ChatPartDto
                {
                    Text = $"§c{qty}x {name}",
                    Hover = hasLbin ? "Click to view the cheapest auction" : "Click to open the auction house",
                    OnClick = hasLbin ? $"/viewauction {lbin[step.ItemTag]}" : "/ah"
                };
            case StepKind.Npc:
                return new ChatPartDto
                {
                    Text = $"§c{qty}x {name}" + (string.IsNullOrEmpty(step.Note) ? "" : $" §7({step.Note})"),
                    Hover = "Buy from an NPC"
                };
            case StepKind.Craft:
                return new ChatPartDto { Text = $"§aCraft {name}: click to open /recipe", Hover = "Click to open the recipe", OnClick = $"/recipe {name}" };
            case StepKind.Forge:
                return new ChatPartDto { Text = $"§aForge {name}", Hover = "Click to warp to the forge", OnClick = "/warp forge" };
            default:
                return new ChatPartDto
                {
                    Text = $"§aSell {name}",
                    Hover = hasCookie ? "Click to open the bazaar" : "Click to open the auction house",
                    OnClick = hasCookie ? $"/bz {name}" : "/ah"
                };
        }
    }

    public static string Hash(InfoDisplayDto dto)
    {
        var sb = new StringBuilder();
        sb.Append(dto.Title).Append('\n').Append(dto.Clear);
        foreach (var l in dto.Lines)
            sb.Append('\n').Append(l.Text).Append('|').Append(l.Hover).Append('|').Append(l.OnClick);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
