using HermesAgent.Sdk.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.AgentAdapter.MicrosoftAgent;

/// <summary>
/// DI extension methods for registering the HermesAgent adapter services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="HermesAgent"/> as a transient service in the DI container.
    /// Requires that <c>AddHermesAgent</c> has already been called to register
    /// the core Hermes SDK clients (<see cref="IHermesResponseClient"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddHermesAgentAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        /*services.TryAddTransient(sp =>
        {
            var responseClient = sp.GetRequiredService<IHermesResponseClient>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            return responseClient.CreateHermesAgent(loggerFactory: loggerFactory);
        });*/

        services.TryAddTransient<HermesClientAdapter>();

        services.TryAddTransient<IChatClient>(sp =>
        {
            var builder = new ChatClientBuilder(sp.GetRequiredService<HermesClientAdapter>());

             

            return builder.Build(sp);
        });

        return services;
    }
}
