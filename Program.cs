using Pic.Infra3.o10y;
using System.Diagnostics.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Instrumentation.Runtime;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text.Json;

var configurationBuilder = new ConfigurationBuilder();
configurationBuilder.AddJsonFile("appsettings.json");
configurationBuilder.AddJsonFile("appsettings.custom.json");
var configuration = configurationBuilder.Build();

var appBuilder = WebApplication.CreateBuilder(args);

var metricsExporter = configuration.GetValue("UseMetricsExporter", defaultValue: "console")!.ToLowerInvariant();
var tracingExporter = configuration.GetValue("UseTracingExporter", defaultValue: "console")!.ToLowerInvariant();
var logExporter = configuration.GetValue("UseLogExporter", defaultValue: "console")!.ToLowerInvariant();
var histogramAggregation = configuration.GetValue("HistogramAggregation", defaultValue: "explicit")!.ToLowerInvariant();

appBuilder.Services.AddSingleton<Instrumentation>();
Console.WriteLine("{0}", Instrumentation.MeterName);

appBuilder.Logging.ClearProviders();

appBuilder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(
            serviceName: configuration.GetValue("ServiceName", defaultValue: "otel-test")!,
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            serviceInstanceId: Environment.MachineName))
    .WithMetrics(builder =>
    {
        // Metrics

        // Ensure the MeterProvider subscribes to any custom Meters.
        builder
            .AddAspNetCoreInstrumentation()
            .AddMeter(Instrumentation.MeterName)
            .SetExemplarFilter(ExemplarFilterType.TraceBased)
            .AddRuntimeInstrumentation()
            .AddHttpClientInstrumentation();

        switch (histogramAggregation)
        {
            case "exponential":
                builder.AddView(instrument =>
                {
                    return instrument.GetType().GetGenericTypeDefinition() == typeof(Histogram<>)
                        ? new Base2ExponentialBucketHistogramConfiguration()
                        : null;
                });
                break;
            default:
                // Explicit bounds histogram is the default.
                // No additional configuration necessary.
                break;
        }

        switch (metricsExporter)
        {
            case "prometheus":
                builder.AddPrometheusExporter();
                break;
            case "otlp":
                builder.AddOtlpExporter(otlpOptions =>
                {
                    // Use IConfiguration directly for Otlp exporter endpoint option.
                    otlpOptions.Endpoint = new Uri(configuration.GetValue("Otlp:Endpoint", defaultValue: "http://localhost:4317")!);
                });
                break;
            default:
                builder.AddConsoleExporter((exporterOptions, metricReaderOptions) =>
                    {
                        // print metrics per 5 seconds to console
                        metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
                    });
                break;
        }
    })
    .WithTracing(builder =>
    {
        // Ensure the TracerProvider subscribes to any custom ActivitySources.
        builder
            .AddSource(Instrumentation.ActivitySourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddHttpClientInstrumentation()
            .AddAspNetCoreInstrumentation();

        // Use IConfiguration binding for AspNetCore instrumentation options.
        appBuilder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(configuration.GetSection("AspNetCoreInstrumentation"));

        switch (tracingExporter)
        {
            case "zipkin":
                builder.AddZipkinExporter();

                builder.ConfigureServices(services =>
                {
                    // Use IConfiguration binding for Zipkin exporter options.
                    services.Configure<ZipkinExporterOptions>(configuration.GetSection("Zipkin"));
                });
                break;

            case "otlp":
                builder.AddOtlpExporter(otlpOptions =>
                {
                    // Use IConfiguration directly for Otlp exporter endpoint option.
                    otlpOptions.Endpoint = new Uri(configuration.GetValue("Otlp:Endpoint", defaultValue: "http://localhost:4317")!);
                });
                break;
            default:
                builder.AddConsoleExporter();
                break;
        }
    })
    .WithLogging(builder =>
    {
        // Note: See appsettings.json Logging:OpenTelemetry section for configuration.

        switch (logExporter)
        {
            case "otlp":
                builder.AddOtlpExporter(otlpOptions =>
                {
                    // Use IConfiguration directly for Otlp exporter endpoint option.
                    otlpOptions.Endpoint = new Uri(configuration.GetValue("Otlp:Endpoint", defaultValue: "http://localhost:4317")!);
                    // otlpOptions.Protocol = OtlpExportProtocol.Grpc;
                    // otlpOptions.Headers = $"{headerKey}={headerValue}";
                });
                break;
            default:
                builder.AddConsoleExporter();
                break;
        }
    });

appBuilder.Services.AddControllers();
appBuilder.Services.AddEndpointsApiExplorer();
appBuilder.Services.AddSwaggerGen();

var app = appBuilder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

if (metricsExporter.Equals("prometheus", StringComparison.OrdinalIgnoreCase))
{
    app.UseOpenTelemetryPrometheusScrapingEndpoint();
}

app.MapGet("/", async (ILogger<Program> logger, ActivitySource activitySource, HttpContext context) =>
{
    using (var activity = activitySource.StartActivity("MyCustomOperation"))
    {
        var currentTime = DateTime.UtcNow.ToString();
        logger.LogInformation($"Application Status changed to runnging at '{currentTime}'");

        var callSelfData = await GetSelfData(logger, activitySource, context);
        logger.LogInformation($"Data: {callSelfData}");

        return $"Hello OpenTelemetry, here's my activity id: {Activity.Current?.Id}\n";
    }
});

app.MapGet("/increase-days", (Instrumentation metrics) => {
    metrics.AddDays(1);
});

app.MapGet("/hello", () => "test hello");

app.Logger.StartingApp();
app.Run();

async Task<string> GetSelfData(ILogger logger, ActivitySource activitySource, HttpContext context)
{
    using (var httpClient = new HttpClient())
    {
        var request = context.Request.HttpContext.Request;
        var url = new Uri($"{request.Scheme}://{request.Host}/hello");
        var response = await httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation($"Response from the External API {content}");
            return content;
        }

        logger.LogError("Unable to fetch data from the external API.");
        return "Unable to fetch data";
    }
}

public static partial class ApplicationLogs
{
    [LoggerMessage(1, LogLevel.Information, "Starting the app...")]
    public static partial void StartingApp(this ILogger logger);

}