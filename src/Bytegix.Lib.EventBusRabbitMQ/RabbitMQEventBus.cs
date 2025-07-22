using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Bytegix.Lib.EventBus.Abstractions;
using Bytegix.Lib.EventBus.Events;
using Bytegix.Lib.EventBusRabbitMQ.Extensions;
using Bytegix.Lib.EventBusRabbitMQ.Settings;
using Bytegix.Lib.EventBusRabbitMQ.Utils;
using Polly.Retry;
using Bytegix.Lib.EventBusRabbitMQ.Telemetry;

namespace Bytegix.Lib.EventBusRabbitMQ;

public sealed class RabbitMQEventBus : IEventBus, IDisposable, IHostedService
{
    // Constants
    // ==============================
    private readonly string _exchangeName;
    private readonly ActivitySource _activitySource;

    private readonly ILogger<RabbitMQEventBus> _logger;

    // Fields
    // ==============================
    private readonly ResiliencePipeline _pipeline;
    private readonly TextMapPropagator _propagator;
    private readonly string _queueName;
    private readonly IServiceProvider _serviceProvider;
    private readonly EventBusSubscriptionInfo _subscriptionInfo;
    private IChannel? _consumerChannel;
    private IConnection? _rabbitMQConnection;

    // Constructor
    // ==============================
    public RabbitMQEventBus(ILogger<RabbitMQEventBus> logger,
        IServiceProvider serviceProvider,
        IOptions<EventBusSettings> options,
        IOptions<EventBusSubscriptionInfo> subscriptionOptions,
        RabbitMQTelemetry rabbitMQTelemetry)
    {
        _pipeline = CreateResiliencePipeline(options.Value.RetryCount);
        _queueName = options.Value.SubscriptionClientName ??
                     throw new ApplicationException("Subscription client name is not set in RabbitMQ settings");
        _exchangeName = options.Value.ExchangeName;
        _activitySource = rabbitMQTelemetry.ActivitySource;
        _propagator = rabbitMQTelemetry.Propagator;
        _subscriptionInfo = subscriptionOptions.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public void Dispose()
    {
        _consumerChannel?.Dispose();
    }

    // TODO: Implement Result for return type
    public async Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var routingKey = @event.GetType().Name;

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id,
                routingKey);

        if (_rabbitMQConnection is null)
            throw new ApplicationException("RabbitMQ connection is not initialized");

        await using var channel = await _rabbitMQConnection.CreateChannelAsync(cancellationToken: cancellationToken) ??
                            throw new ApplicationException("RabbitMQ connection is not initialized");

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

        await channel.ExchangeDeclareAsync(_exchangeName, "direct", cancellationToken: cancellationToken);

        var body = SerializationUtils.SerializeMessage(@event, _subscriptionInfo);

        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        var activityName = $"{routingKey} publish";

        // We call the helper method to perform the actual logic
        await ExecutePublishAsync(channel, routingKey, body, @event, activityName, cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Messaging is async so we don't need to wait for it to complete. On top of this
        // the APIs are blocking, so we need to run this on a background thread.
        try
        {
            _logger.LogInformation("Starting RabbitMQ connection on a background thread");

            _rabbitMQConnection = _serviceProvider.GetRequiredService<IConnection>();
            if (!_rabbitMQConnection.IsOpen) return;

            if (_logger.IsEnabled(LogLevel.Trace)) _logger.LogTrace("Creating RabbitMQ consumer channel");

            _consumerChannel = await _rabbitMQConnection.CreateChannelAsync(cancellationToken: cancellationToken);

            _consumerChannel.CallbackExceptionAsync += async (_, ea) =>
            {
                _logger.LogWarning(ea.Exception, "Error with RabbitMQ consumer channel");
                await Task.CompletedTask;
            };

            await _consumerChannel.ExchangeDeclareAsync(_exchangeName,
                "direct", cancellationToken: cancellationToken);

            await _consumerChannel.QueueDeclareAsync(_queueName,
                true,
                false,
                false,
                cancellationToken: cancellationToken);

            if (_logger.IsEnabled(LogLevel.Trace)) _logger.LogTrace("Starting RabbitMQ basic consume");

            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

            consumer.ReceivedAsync += OnMessageReceived;

            await _consumerChannel.BasicConsumeAsync(_queueName, false, consumer, cancellationToken);

            foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
                await _consumerChannel.QueueBindAsync(
                    _queueName,
                    _exchangeName,
                    eventName, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting RabbitMQ connection");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task ExecutePublishAsync(IChannel channel, string routingKey, byte[] body, IntegrationEvent @event,
        string activityName, CancellationToken cancellationToken)
    {
        await _pipeline.ExecuteAsync(async token =>
        {
            using var activity = _activitySource.StartActivity(activityName, ActivityKind.Client) ??
                                 throw new ApplicationException("Activity is not initialized");

            ActivityContext contextToInject;

            contextToInject = activity.Context;
            if (Activity.Current != null) contextToInject = Activity.Current.Context;

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
            };

            _propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), properties,
                InjectTraceContextIntoBasicProperties);

            RabbitMQTelemetryHelpers.SetMessageActivityContext(activity, routingKey, "publish");

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("Publishing event to RabbitMQ: {EventId}", @event.Id);

            try
            {
                await channel.BasicPublishAsync(
                    _exchangeName,
                    routingKey,
                    true,
                    properties,
                    body, cancellationToken: token);
            }
            catch (Exception ex)
            {
                activity.SetExceptionTags(ex);
                throw;
            }

            return;

            static void InjectTraceContextIntoBasicProperties(IBasicProperties props, string key, string value)
            {
                props.Headers ??= new Dictionary<string, object>()!;
                props.Headers[key] = value;
            }
        }, cancellationToken);
    }

    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs eventArgs)
    {
        if (_consumerChannel is null) throw new ApplicationException("Consumer channel is not initialized");

        // Extract the PropagationContext of the upstream parent from the message headers.
        var parentContext =
            _propagator.Extract(default, eventArgs.BasicProperties, ExtractTraceContextFromBasicProperties);
        Baggage.Current = parentContext.Baggage;

        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        var activityName = $"{eventArgs.RoutingKey} receive";

        using var activity =
            _activitySource.StartActivity(activityName, ActivityKind.Client, parentContext.ActivityContext);

        if (activity is null) throw new ApplicationException("Activity is not initialized");

        RabbitMQTelemetryHelpers.SetMessageActivityContext(activity, eventArgs.RoutingKey, "receive");

        var eventName = eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        try
        {
            activity.SetTag("message", message);

            if (message.Contains("throw-fake-exception", StringComparison.InvariantCultureIgnoreCase))
                throw new InvalidOperationException($"Fake exception requested: \"{message}\"");

            await ProcessEvent(eventName, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Processing message \"{Message}\"", message);

            activity.SetExceptionTags(ex);
        }

        // Even on exception we take the message off the queue.
        // in a REAL WORLD app this should be handled with a Dead Letter Exchange (DLX).
        // For more information see: https://www.rabbitmq.com/dlx.html
        await _consumerChannel.BasicAckAsync(eventArgs.DeliveryTag, false);
        return;

        static IEnumerable<string> ExtractTraceContextFromBasicProperties(IReadOnlyBasicProperties props, string key)
        {
            if (props.Headers == null || !props.Headers.TryGetValue(key, out var value))
                return [];

            if (value is byte[] bytes)
                return [Encoding.UTF8.GetString(bytes)];

            return [];
        }
    }

    // Helper Methods
    // ==============================
    private async Task ProcessEvent(string eventName, string message)
    {
        if (_logger.IsEnabled(LogLevel.Trace)) _logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName);

        await using var scope = _serviceProvider.CreateAsyncScope();

        if (!_subscriptionInfo.EventTypes.TryGetValue(eventName, out var eventType))
        {
            _logger.LogWarning("Unable to resolve event type for event name {EventName}", eventName);
            return;
        }

        // Deserialize the event
        var integrationEvent = SerializationUtils.DeserializeMessage(message, eventType, _subscriptionInfo);

        // REVIEW: This could be done in parallel

        // Get all the handlers using the event type as the key
        foreach (var handler in scope.ServiceProvider.GetKeyedServices<IIntegrationEventHandler>(eventType))
            await handler.Handle(integrationEvent);
    }

    private static ResiliencePipeline CreateResiliencePipeline(int retryCount)
    {
        // See https://www.pollydocs.org/strategies/retry.html
        var retryOptions = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<BrokerUnreachableException>().Handle<SocketException>(),
            MaxRetryAttempts = retryCount,
            DelayGenerator = context => ValueTask.FromResult(GenerateDelay(context.AttemptNumber))
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .Build();

        static TimeSpan? GenerateDelay(int attempt)
        {
            return TimeSpan.FromSeconds(Math.Pow(2, attempt));
        }
    }
}
