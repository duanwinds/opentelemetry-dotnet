namespace Pic.Infra3.o10y;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "Pic.Infra3.o10y";
    internal const string MeterName = "Pic.Infra3.o10y";
    private readonly Meter meter;

    public Instrumentation()
    {
        string? version = typeof(Instrumentation).Assembly.GetName().Version?.ToString();
        this.ActivitySource = new ActivitySource(ActivitySourceName, version);
        this.meter = new Meter(MeterName, version);
        this.FreezingDaysCounter = this.meter.CreateCounter<long>("weather.days.freezing", description: "The number of days where the temperature is below freezing");
    }

    public ActivitySource ActivitySource { get; }

    public Counter<long> FreezingDaysCounter { get; }

    public void Dispose()
    {
        this.ActivitySource.Dispose();
        this.meter.Dispose();
    }
}