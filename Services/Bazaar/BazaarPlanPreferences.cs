namespace Coflnet.Sky.Commands.Shared
{
    /// <summary>Translates the user's bazaar plan settings into planner request parameters.</summary>
    public static class BazaarPlanPreferences
    {
        public const int DefaultHorizonHours = 24;

        public static (string[] Strategies, double? InvestingHorizonHours, long BudgetCapCoins) Resolve(ModSettings settings)
        {
            settings ??= new ModSettings();
            var strategies = settings.BazaarMode switch
            {
                BazaarPlanMode.Investing => new[] { "Investing" },
                BazaarPlanMode.Both => new[] { "Active", "Investing" },
                _ => new[] { "Active" }
            };
            var investing = settings.BazaarMode != BazaarPlanMode.Active;
            double? horizon = investing
                ? (settings.BazaarInvestingHorizonHours == 0 ? DefaultHorizonHours : settings.BazaarInvestingHorizonHours)
                : null;
            return (strategies, horizon, settings.BazaarPlanBudget);
        }
    }
}
