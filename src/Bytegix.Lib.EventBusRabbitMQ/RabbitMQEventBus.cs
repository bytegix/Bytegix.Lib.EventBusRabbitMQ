using Bytegix.Lib.EventBus.Abstractions;
using Bytegix.Lib.EventBus.Events;
using Bytegix.Lib.EventBus.Subscription;
using Bytegix.Lib.EventBusRabbitMQ.Settings;
using Bytegix.Lib.EventBusRabbitMQ.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Bytegix.Lib.EventBusRabbitMQ.Exceptions;
using Bytegix.Lib.EventBusRabbitMQ.Utils;

namespace Bytegix.Lib.EventBusRabbitMQ;
public sealed class RabbitMQEventBus : IEventBus, IDisposable, IHostedService
{
    // Constants
    // ==============================
    private const string ExchangeName = "eshop_event_bus";
    private readonly ActivitySource _activitySource;

    private readonly ILogger<RabbitMQEventBus> _logger;

    // Fields
    // ==============================
    private readonly ResiliencePipeline _pipeline;
    private readonly TextMapPropagator _propagator;
    private readonly string _queueName;
    private readonly IServiceProvider _serviceProvider;
    private readonly EventBusSubscriptionInfo _subscriptionInfo;
    private IModel ? _consumerChannel;
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
        _queueName = options.Value?.SubscriptionClientName ??
                     throw new ApplicationException("Subscription client name is not set in RabbitMQ settings");
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

    public Task PublishAsync(IntegrationEvent @event)
    {
        var routingKey = @event.GetType().Name;

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id,
                routingKey);

        using var channel = _rabbitMQConnection?.CreateModel() ??
                            throw new ApplicationException("RabbitMQ connection is not initialized");

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

        channel.ExchangeDeclare(ExchangeName, "direct");

        var body = SerializationUtils.SerializeMessage(@event, _subscriptionInfo);

        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        var activityName = $"{routingKey} publish";

        // We call the helper method to perform the actual logic
        return ExecutePublishAsync(channel, routingKey, body, @event, activityName);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Messaging is async so we don't need to wait for it to complete. On top of this
        // the APIs are blocking, so we need to run this on a background thread.
        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                _logger.LogInformation("Starting RabbitMQ connection on a background thread");

                _rabbitMQConnection = _serviceProvider.GetRequiredService<IConnection>();
                if (!_rabbitMQConnection.IsOpen) return;

                if (_logger.IsEnabled(LogLevel.Trace)) _logger.LogTrace("Creating RabbitMQ consumer channel");

                _consumerChannel = _rabbitMQConnection.CreateModel();

                _consumerChannel.CallbackException += (_, ea) =>
                {
                    _logger.LogWarning(ea.Exception, "Error with RabbitMQ consumer channel");
                };

                _consumerChannel.ExchangeDeclare(ExchangeName,
                    "direct");

                _consumerChannel.QueueDeclare(_queueName,
                    true,
                    false,
                    false,
                    null);

                if (_logger.IsEnabled(LogLevel.Trace)) _logger.LogTrace("Starting RabbitMQ basic consume");

                var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

                consumer.Received += OnMessageReceived;

                _consumerChannel.BasicConsume(_queueName, false, consumer);

                foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
                    _consumerChannel.QueueBind(
                        _queueName,
                        ExchangeName,
                        eventName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting RabbitMQ connection");
            }
        },
            TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private Task ExecutePublishAsync(IModel channel, string routingKey, byte[] body, IntegrationEvent @event,
        string activityName)
    {
        return _pipeline.Execute(() =>
        {
            using var activity = _activitySource.StartActivity(activityName, ActivityKind.Client) ??
                                 throw new ApplicationException("Activity is not initialized");
            ;

            ActivityContext contextToInject = default;

            contextToInject = activity.Context;
            if (Activity.Current != null) contextToInject = Activity.Current.Context;

            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // persistent

            static void InjectTraceContextIntoBasicProperties(IBasicProperties props, string key, string value)
            {
                props.Headers ??= new Dictionary<string, object>();
                props.Headers[key] = value;
            }

            _propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), properties,
                InjectTraceContextIntoBasicProperties);

            SetActivityContext(activity, routingKey, "publish");

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("Publishing event to RabbitMQ: {EventId}", @event.Id);

            try
            {
                channel.BasicPublish(
                    ExchangeName,
                    routingKey,
                    true,
                    properties,
                    body);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                activity.SetExceptionTags(ex);
                throw;
            }
        });
    }

    private static void SetActivityContext(Activity activity, string routingKey, string operation)
    {
        // These tags are added demonstrating the semantic conventions of the OpenTelemetry messaging specification
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.destination_kind", "queue");
        activity.SetTag("messaging.operation", operation);
        activity.SetTag("messaging.destination.name", routingKey);
        activity.SetTag("messaging.rabbitmq.routing_key", routingKey);
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

        SetActivityContext(activity, eventArgs.RoutingKey, "receive");

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
        _consumerChannel.BasicAck(eventArgs.DeliveryTag, false);
        return;

        static IEnumerable<string> ExtractTraceContextFromBasicProperties(IBasicProperties props, string key)
        {
            if (!props.Headers.TryGetValue(key, out var value)) return [];

            var bytes = value as byte[];

            return [Encoding.UTF8.GetString(bytes!)];
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
