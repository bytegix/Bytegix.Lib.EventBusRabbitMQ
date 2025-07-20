using Bytegix.Lib.EventBus.Abstractions;
using Bytegix.Lib.EventBusRabbitMQ.Settings;
using Bytegix.Lib.EventBusRabbitMQ.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

public static class RabbitMqDependencyInjectionExtensions
{
    public static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, EventBusSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddRabbitMqClient(configure =>
        {
            configure.HostName = settings.HostName;
            configure.UserName = settings.UserName;
            configure.Password = settings.Password;
            configure.VirtualHost = settings.VirtualHost;
            configure.Port = settings.Port;
            configure.ConsumerDispatchConcurrency = settings.ConsumerDispatchConcurrency;
        });

        // RabbitMQ.Client doesn't have built-in support for OpenTelemetry, so we need to add it ourselves
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => { tracing.AddSource(RabbitMQTelemetry.ActivitySourceName); });

        // Options support
        builder.Services.Configure<EventBusSettings>(builder.Configuration.GetSection(EventBusSettings.SectionName));

        // Abstractions on top of the core client API
        builder.Services.AddSingleton<RabbitMQTelemetry>();
        builder.Services.AddSingleton<IEventBus, RabbitMQEventBus>();
        // Start consuming messages as soon as the application starts
        builder.Services.AddSingleton<IHostedService>(sp => (RabbitMQEventBus)sp.GetRequiredService<IEventBus>());

        return new EventBusBuilder(builder.Services);
    }

    public static IServiceCollection AddRabbitMqClient(this IServiceCollection services, Action<ConnectionFactory> configure)
    {
        services.AddSingleton<IConnectionFactory>(sp =>
        {
            var factory = new ConnectionFactory();
            configure.Invoke(factory);
            return factory;
        });

        // Register a factory for IConnection using async
        services.AddSingleton<Func<Task<IConnection>>>(sp =>
        {
            var factory = sp.GetRequiredService<IConnectionFactory>();
            return () => factory.CreateConnectionAsync();
        });

        return services;
    }

    private class EventBusBuilder(IServiceCollection services) : IEventBusBuilder
    {
        public IServiceCollection Services => services;
    }
}
