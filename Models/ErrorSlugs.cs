namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Error slugs that several projects need to agree on
/// </summary>
public static class ErrorSlugs
{
    /// <summary>
    /// Raised when an action needs a user account but the connection is not signed in
    /// </summary>
    public const string NoLogin = "no_login";
}
