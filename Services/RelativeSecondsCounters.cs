using Prometheus;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Monotonic early/late counters next to an api relative histogram. The histogram _sum goes down for negative
/// observations and prometheus reads that as a counter reset, so long window means need these instead:
/// mean = (rate(late) - rate(early)) / rate(histogram_count).
/// </summary>
public class RelativeSecondsCounters
{
    private readonly Counter early;
    private readonly Counter late;

    /// <param name="baseName">histogram name without the _seconds suffix, eg sky_mod_flip_send_relative_api</param>
    public RelativeSecondsCounters(string baseName, MetricFactory factory = null)
    {
        factory ??= Metrics.DefaultFactory;
        var help = $"Summed absolute seconds of {baseName}_seconds observations that were %s the api arrival. "
            + $"Mean seconds = (rate({baseName}_late_seconds_total) - rate({baseName}_early_seconds_total)) / rate({baseName}_seconds_count)";
        early = factory.CreateCounter($"{baseName}_early_seconds_total", help.Replace("%s", "before"), "tier", "source");
        late = factory.CreateCounter($"{baseName}_late_seconds_total", help.Replace("%s", "after"), "tier", "source");
    }

    public void Add(string tier, string source, double seconds)
    {
        if (seconds < 0)
            early.WithLabels(tier, source).Inc(-seconds);
        else if (seconds > 0)
            late.WithLabels(tier, source).Inc(seconds);
    }

    public double Early(string tier, string source) => early.WithLabels(tier, source).Value;
    public double Late(string tier, string source) => late.WithLabels(tier, source).Value;
}
