using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Serilog;
using TriasDeparture.Alexa.HomeStops;
using TriasDeparture.Alexa.Models;
using TriasDeparture.Alexa.Skill;
using TriasDeparture.Alexa.Verification;
using TriasDeparture.Infrastructure.Extensions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .WriteTo.Console());

    // ── TRIAS (shared with the API) ──────────────────────────────────────────
    builder.Services.AddInfrastructure(builder.Configuration);

    // ── Alexa ────────────────────────────────────────────────────────────────
    builder.Services.AddOptions<AlexaOptions>()
        .Bind(builder.Configuration.GetSection(AlexaOptions.SectionName))
        .Validate(o => !o.VerifyRequests || !string.IsNullOrWhiteSpace(o.SkillId),
                  $"{AlexaOptions.SectionName}:SkillId is required when request verification is enabled.")
        .ValidateOnStart();

    builder.Services.AddHttpClient(AlexaRequestVerifier.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(5));
    builder.Services.AddSingleton<AlexaRequestVerifier>();
    builder.Services.AddScoped<SlotResolver>();
    builder.Services.AddScoped<DepartureAnswerService>();
    builder.Services.AddScoped<SkillHandler>();

    // ── Home stop store: Firestore in production, in-memory for local development ──
    builder.Services.Configure<FirestoreOptions>(builder.Configuration.GetSection(FirestoreOptions.SectionName));
    if (string.Equals(builder.Configuration["HomeStops:Store"], "InMemory", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddSingleton<IHomeStopStore, InMemoryHomeStopStore>();
    else
        builder.Services.AddSingleton<IHomeStopStore, FirestoreHomeStopStore>();

    // ────────────────────────────────────────────────────────────────────────
    var app = builder.Build();
    // ────────────────────────────────────────────────────────────────────────

    app.UseSerilogRequestLogging();

    app.MapGet("/", () => "glei Alexa endpoint");

    app.MapPost("/", async (HttpRequest http, AlexaRequestVerifier verifier, SkillHandler handler,
                            ILogger<Program> logger, CancellationToken ct) =>
    {
        // The signature covers the exact bytes, so read the raw body before deserializing
        using var buffer = new MemoryStream();
        await http.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();

        SkillRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SkillRequest>(body, AlexaJson.Options);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request?.Request is null)
            return Results.BadRequest();

        if (await verifier.VerifyAsync(http.Headers, body, request, ct) is { } rejection)
        {
            logger.LogWarning("Rejected Alexa request: {Reason}", rejection);
            return Results.BadRequest();
        }

        var response = await handler.HandleAsync(request, ct);
        return Results.Json(response, AlexaJson.Options);
    });

    var alexa = app.Services.GetRequiredService<IOptions<AlexaOptions>>().Value;
    app.Lifetime.ApplicationStarted.Register(() =>
        Log.Information("glei Alexa running on {Urls} ({Environment}) - request verification: {Verify}",
            string.Join(", ", app.Urls), app.Environment.EnvironmentName, alexa.VerifyRequests));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>JSON conventions of the Alexa request/response format.</summary>
internal static class AlexaJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

// Expose for integration tests
public partial class Program { }
