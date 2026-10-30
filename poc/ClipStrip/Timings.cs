namespace ClipStrip;

// Collected durations in milliseconds, summarised for the strip header and the benchmark.
public sealed class Timings
{
    private const double Median = 0.5;
    private const double P95 = 0.95;

    private readonly List<double> _values = [];

    public int Count => _values.Count;

    public void Add(TimeSpan time) => _values.Add(time.TotalMilliseconds);

    public void Clear() => _values.Clear();

    public string Summary()
    {
        if (_values.Count == 0)
            return "–";
        var sorted = _values.Order().ToList();
        return $"avg {sorted.Average():0} · median {At(sorted, Median):0} · p95 {At(sorted, P95):0} · max {sorted[^1]:0} ms";
    }

    private static double At(List<double> sorted, double fraction) =>
        sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(fraction * sorted.Count))];
}
