namespace Coflnet.Sky.Commands.Shared
{
    /// <summary>Which kind of bazaar plans a user wants to receive.</summary>
    public enum BazaarPlanMode
    {
        /// <summary>Fast flips</summary>
        Active = 0,
        /// <summary>Hold up to the investing horizon</summary>
        Investing = 1,
        /// <summary>Both, sharing one budget</summary>
        Both = 2
    }
}
