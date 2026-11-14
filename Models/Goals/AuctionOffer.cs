using System.Text.RegularExpressions;

namespace Coflnet.Sky.Commands.Shared.Goals;

/// <summary>The currently cheapest active BIN auction of an item.</summary>
/// <param name="Uuid">Full auction uuid (32 hex characters, the id <c>/viewauction</c> needs)</param>
/// <param name="Price">Starting bid of the auction</param>
public record AuctionOffer(string Uuid, long Price)
{
    private static readonly Regex FullUuid = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    /// <summary>The uuid without dashes in lower case when it is a full auction uuid, otherwise null.</summary>
    public string ValidUuid()
    {
        var plain = Uuid?.Replace("-", "").ToLowerInvariant();
        return plain != null && FullUuid.IsMatch(plain) ? plain : null;
    }

    /// <summary><c>/viewauction</c> command for the auction, null when the uuid is not a full auction uuid.</summary>
    public string ViewCommand() => ValidUuid() is { } uuid ? $"/viewauction {uuid}" : null;
}
