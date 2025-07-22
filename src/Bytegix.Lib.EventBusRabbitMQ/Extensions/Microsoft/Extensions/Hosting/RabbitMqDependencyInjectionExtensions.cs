using Bytegix.Lib.EventBus.Abstractions;
using Bytegix.Lib.EventBusRabbitMQ;
using Bytegix.Lib.EventBusRabbitMQ.Settings;
using Bytegix.Lib.EventBusRabbitMQ.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

public static class RabbitMqDependencyInjectionExtensions
{
    private const string ActivitySourceName = "Bytegix.Lib.EventBusRabbitMQ";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static IEventBusBuilder AddRabbitMqEventBus(
        this IHostApplicationBuilder builder,
        string host,
        string username,
        string password,
        string subscriptionClientName,
        int port = 5672,
        string vhost = "/",
        Action<RabbitMQConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionClientName);

        var configuration = new RabbitMQConfiguration();

        configure?.Invoke(configuration);

        var settings = new EventBusSettings(
            host,
            username,
            password,
            vhost,
            subscriptionClientName,
            port,
            configuration);

        builder.AddRabbitMqEventBus(settings);

        return new EventBusBuilder(builder.Services);
    }

    public static IEventBusBuilder AddRabbitMqEventBus(
        this IHostApplicationBuilder builder,
        string host,
        string username,
        string password,
        string subscriptionClientName,
        int port = 5672,
        string vhost = "/",
        RabbitMQConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionClientName);

        var settings = new EventBusSettings(
            host,
            username,
            password,
            vhost,
            subscriptionClientName,
            port,
            configuration);

        builder.AddRabbitMqEventBus(settings);

        return new EventBusBuilder(builder.Services);
    }

    public static IEventBusBuilder AddRabbitMqEventBus(
        this IHostApplicationBuilder builder,
        string host,
        string username,
        string password,
        string subscriptionClientName,
        int port = 5672,
        string vhost = "/")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionClientName);

        var settings = new EventBusSettings(
            host,
            username,
            password,
            vhost,
            subscriptionClientName,
            port);

        builder.AddRabbitMqEventBus(settings);

        return new EventBusBuilder(builder.Services);
    }

    public static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, string connectionString, string subscriptionClientName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionClientName);

        var settings = new EventBusSettings(connectionString, subscriptionClientName);

        builder.AddRabbitMqEventBus(settings);


        return new EventBusBuilder(builder.Services);
    }

    public static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, string connectionString, string subscriptionClientName, Action<RabbitMQConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionClientName);

        var configuration = new RabbitMQConfiguration();
        configure.Invoke(configuration);

        var settings = new EventBusSettings(connectionString, subscriptionClientName, configuration);

        builder.AddRabbitMqEventBus(settings);


        return new EventBusBuilder(builder.Services);
    }

    public static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, string connectionString, string subscriptionClientName, RabbitMQConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionClientName);

        var settings = new EventBusSettings(connectionString, subscriptionClientName, configuration);

        builder.AddRabbitMqEventBus(settings);


        return new EventBusBuilder(builder.Services);
    }

    public static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sectionName);

        var settings = builder.Configuration.GetSection(sectionName).Get<EventBusSettings>();

        if (settings is null)
        {
            throw new ArgumentException($"Configuration section '{sectionName}' not found or is invalid.", nameof(sectionName));
        }

        builder.AddRabbitMqEventBus(settings);

        return new EventBusBuilder(builder.Services);
    }

    private static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, EventBusSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.HostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.UserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Password);

        builder.Services.AddRabbitMqClient(configure =>
        {
            configure.HostName = settings.HostName;
            configure.UserName = settings.UserName;
            configure.Password = settings.Password;
            configure.VirtualHost = settings.VirtualHost;
            configure.Port = settings.Port;
            configure.ConsumerDispatchConcurrency = settings.ConsumerDispatchConcurrency;
        }, settings);

        // RabbitMQ.Client doesn't have built-in support for OpenTelemetry, so we need to add it ourselves
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => { tracing.AddSource(RabbitMQTelemetry.ActivitySourceName); });

        // Options support
        builder.Services.Configure<EventBusSettings>(opts =>
        {
            opts.HostName = settings.HostName;
            opts.UserName = settings.UserName;
            opts.Password = settings.Password;
            opts.VirtualHost = settings.VirtualHost;
            opts.Port = settings.Port;
            opts.ConsumerDispatchConcurrency = settings.ConsumerDispatchConcurrency;
            opts.MaximumInboundMessageSize = settings.MaximumInboundMessageSize;
            opts.SubscriptionClientName = settings.SubscriptionClientName;
            opts.RetryCount = settings.RetryCount;
        });

        // Abstractions on top of the core client API
        builder.Services.AddSingleton<RabbitMQTelemetry>();
        builder.Services.AddSingleton<IEventBus, RabbitMQEventBus>();
        // Start consuming messages as soon as the application starts
        builder.Services.AddSingleton<IHostedService>(sp => (RabbitMQEventBus)sp.GetRequiredService<IEventBus>());

        return new EventBusBuilder(builder.Services);
    }

    private static IServiceCollection AddRabbitMqClient(this IServiceCollection services, Action<ConnectionFactory> configure, EventBusSettings settings)
    {
        services.AddSingleton<IConnectionFactory>(sp =>
        {
            var factory = new ConnectionFactory
            {
                ConsumerDispatchConcurrency = settings.ConsumerDispatchConcurrency
            };
            configure.Invoke(factory);
            return factory;
        });

        // Register a factory for IConnection using async
        services.AddSingleton<IConnection>(sp =>
        {
            var factory = sp.GetRequiredService<IConnectionFactory>();
            return CreateConnection(factory, settings.RetryCount);
        });

        services.AddSingleton<RabbitMQEventSourceLogForwarder>();

        if (!settings.DisableTracing)
        {
            services.AddOpenTelemetry()
                .WithTracing(traceBuilder =>
                        traceBuilder
                            .AddSource(ActivitySourceName)
                            .AddSource("RabbitMQ.Client.*")
                );
        }

        return services;
    }

    private static IConnection CreateConnection(IConnectionFactory factory, int retryCount)
    {
        var resiliencePipelineBuilder = new ResiliencePipelineBuilder();
        if (retryCount > 0)
        {
            resiliencePipelineBuilder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = static args => args.Outcome is { Exception: SocketException or BrokerUnreachableException }
                    ? PredicateResult.True()
                    : PredicateResult.False(),
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = retryCount,
                Delay = TimeSpan.FromSeconds(1),
            });
        }
        var resiliencePipeline = resiliencePipelineBuilder.Build();

        using var activity = ActivitySource.StartActivity("rabbitmq connect", ActivityKind.Client);
        RabbitMQTelemetryHelpers.AddRabbitMQTags(activity, factory.Uri);

        return resiliencePipeline.ExecuteAsync(static async (factory, cancellationToken) =>
        {
            using var connectAttemptActivity = ActivitySource.StartActivity("rabbitmq connect attempt", ActivityKind.Client);
            RabbitMQTelemetryHelpers.AddRabbitMQTags(connectAttemptActivity, factory.Uri, "connect");

            try
            {
                return await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RabbitMQTelemetryHelpers.AddRabbitMQExceptionTags(connectAttemptActivity, ex);
                throw;
            }
        }, factory).AsTask().GetAwaiter().GetResult(); // see https://github.com/dotnet/aspire/issues/565

    }

    private class EventBusBuilder(IServiceCollection services) : IEventBusBuilder
    {
        public IServiceCollection Services => services;
    }
}
