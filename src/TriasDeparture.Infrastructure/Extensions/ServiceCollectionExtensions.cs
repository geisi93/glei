using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using TriasDeparture.Application.Interfaces;
using TriasDeparture.Infrastructure.Configuration;
using TriasDeparture.Infrastructure.Http;

namespace TriasDeparture.Infrastructure.Extensions;

/// <summary>DI registration for the Infrastructure layer.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Fail fast on startup instead of with obscure errors on the first request
        services.AddOptions<TriasOptions>()
            .Bind(configuration.GetSection(TriasOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                      $"{TriasOptions.SectionName}:BaseUrl must be an absolute URL.")
            .ValidateOnStart();

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient<ITriasClient, TriasHttpClient>(http =>
            http.Timeout = TimeSpan.FromSeconds(10));

        return services;
    }
}
