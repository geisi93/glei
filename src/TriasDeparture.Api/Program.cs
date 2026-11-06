using Serilog;
using TriasDeparture.Api;
using TriasDeparture.Application.Services;
using TriasDeparture.Infrastructure.Extensions;

// ────────────────────────────────────────────────────────────────────────────
// Bootstrap Serilog early so startup errors are captured
// ────────────────────────────────────────────────────────────────────────────
// Umlauts in stop names and log messages render correctly on Windows consoles
Console.OutputEncoding = System.Text.Encoding.UTF8;

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

    // ── Infrastructure (TRIAS client, caching) ───────────────────────────────
    builder.Services.AddInfrastructure(builder.Configuration);

    // ── Application services ─────────────────────────────────────────────────
    builder.Services.AddScoped<DepartureService>();

    // ── Web API + Swagger ─────────────────────────────────────────────────────
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<TriasExceptionHandler>();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title       = "glei",
            Version     = "v1",
            Description = "Live departures from the Verbund Linie TRIAS interface"
        });
        // Include XML documentation comments
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            c.IncludeXmlComments(xmlPath);
    });

    // ────────────────────────────────────────────────────────────────────────
    var app = builder.Build();
    // ────────────────────────────────────────────────────────────────────────

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "glei v1");
            c.RoutePrefix = string.Empty; // Swagger at root
        });
    }

    app.UseHttpsRedirection();
    app.MapControllers();

    // Startup banner with the actual bound URLs (known only once the server has started)
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information("glei running on {Urls} ({Environment}) - TRIAS: {TriasUrl}",
            string.Join(", ", app.Urls), app.Environment.EnvironmentName, builder.Configuration["Trias:BaseUrl"]);
        if (app.Environment.IsDevelopment())
            Log.Information("Swagger UI: {SwaggerUrl}", app.Urls.First());
    });

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

// Expose for integration test factory
public partial class Program { }
