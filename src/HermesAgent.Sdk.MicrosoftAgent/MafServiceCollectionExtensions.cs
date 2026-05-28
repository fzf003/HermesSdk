using HermesAgent.Sdk.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// DI extension methods for registering the MAF integration services.
/// </summary>
public static class MafServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MAF integration services, including <see cref="IChatClient"/> adapter.
    ///
    /// <para>Requires that <c>AddHermesAgent</c> has already been called to register the
    /// core Hermes SDK clients.</para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="configureOptions">Optional delegate to configure MAF options.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddHermesAgentMaf(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<HermesAgentMafOptions>? configureOptions = null)
    {
        var mafOptions = new HermesAgentMafOptions();
        configuration.GetSection("HermesAgent:Maf").Bind(mafOptions);
        configureOptions?.Invoke(mafOptions);
        services.Configure<HermesAgentMafOptions>(configuration.GetSection("HermesAgent:Maf"));

        services.TryAddTransient<HermesChatClientAdapter>();

        services.TryAddTransient<IChatClient>(sp =>
        {
            var builder = new ChatClientBuilder(sp.GetRequiredService<HermesChatClientAdapter>());

            if (mafOptions.EnableAutoSession)
            {
                builder.Use((innerClient, _) => new AutoSessionMiddleware(innerClient));
            }

            if (mafOptions.EnableRunMiddleware)
            {
                var runClient = sp.GetRequiredService<IHermesRunClient>();
                var innerLogger = sp.GetRequiredService<ILogger<HermesRunMiddleware>>();
                builder.Use((innerClient, _) =>
                    new HermesRunMiddleware(innerClient, runClient, innerLogger));
            }

            if (mafOptions.EnableOpenTelemetry)
            {
                builder.UseOpenTelemetry();
            }

            return builder.Build(sp);
        });

        return services;
    }
}
