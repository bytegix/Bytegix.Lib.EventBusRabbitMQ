using Bytegix.Lib.EventBus;
using Bytegix.Lib.EventBus.Abstractions;
using Bytegix.Lib.EventBus.Events;
using Bytegix.Lib.EventBusRabbitMQ.Extensions;
using Bytegix.Lib.EventBusRabbitMQ.Settings;
using Bytegix.Lib.EventBusRabbitMQ.Telemetry;
using Bytegix.Lib.EventBusRabbitMQ.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Polly.Retry;
using System.Diagnostics;

namespace Bytegix.Lib.EventBusRabbitMQ;

public sealed class RabbitMQEventBus : IEventBus, IDisposable, IHostedService
{
    // Fields
    // ==============================
    private readonly ResiliencePipeline _pipeline;
    private readonly TextMapPropagator _propagator;
    private readonly string _queueName;
    private readonly string _deadLetterQueueName;
    private readonly string _exchangeName;
    private readonly string _deadLetterExchangeName;
    private readonly IServiceProvider _serviceProvider;
    private readonly EventBusSubscriptionInfo _subscriptionInfo;
    private IChannel? _consumerChannel;
    private IConnection? _rabbitMQConnection;
    private readonly EventBusSettings _eventBusSettings;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<RabbitMQEventBus> _logger;

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
        _deadLetterQueueName = $"{_queueName}{EventBusConstants.DeadLetterSuffix}";
        _exchangeName = options.Value.ExchangeName;
        _deadLetterExchangeName = $"{_exchangeName}{EventBusConstants.DeadLetterSuffix}";
        _activitySource = rabbitMQTelemetry.ActivitySource;
        _propagator = rabbitMQTelemetry.Propagator;
        _subscriptionInfo = subscriptionOptions.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _eventBusSettings = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Messaging is async so we don't need to wait for it to complete. On top of this
        // the APIs are blocking, so we need to run this on a background thread.
        try
        {
            _logger.LogInformation("Starting RabbitMQ connection on a background thread");

            _rabbitMQConnection = _serviceProvider.GetRequiredService<IConnection>();
            if (!_rabbitMQConnection.IsOpen)
            {
                _logger.LogError("RabbitMQ connection is not open. Cannot start consumer channel.");
                return;
            }

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Creating RabbitMQ consumer channel");
            }

            _consumerChannel = await _rabbitMQConnection.CreateChannelAsync(cancellationToken: cancellationToken);

            _consumerChannel.CallbackExceptionAsync += async (_, ea) =>
            {
                _logger.LogCritical(ea.Exception, "Error with RabbitMQ consumer channel");
                Environment.FailFast("Critical error in StartAsync", ea.Exception);
            };

            await _consumerChannel.ExchangeDeclareAsync(_exchangeName,
                "direct", cancellationToken: cancellationToken);
            await _consumerChannel.ExchangeDeclareAsync(_deadLetterExchangeName,
                "direct", cancellationToken: cancellationToken);

            var args = new Dictionary<string, object?>()
            {
                {"x-dead-letter-exchange", _deadLetterExchangeName},
                {"x-delivery-limit", _eventBusSettings.RetryCount},
                {"x-queue-type", "quorum"}
            };
            await _consumerChannel.QueueDeclareAsync(_queueName,
                true,
                false,
                false,
                arguments: args,
                cancellationToken: cancellationToken);
            
            await _consumerChannel.QueueDeclareAsync(_deadLetterQueueName,
                true,
                false,
                false,
                cancellationToken: cancellationToken);

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Starting RabbitMQ basic consume");
            }

            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

            consumer.ReceivedAsync += OnMessageReceived;

            await _consumerChannel.BasicConsumeAsync(_queueName, false, consumer, cancellationToken);
            
            var deadLetterConsumer = new AsyncEventingBasicConsumer(_consumerChannel);

            deadLetterConsumer.ReceivedAsync += OnDeadLetterMessageReceived;

            await _consumerChannel.BasicConsumeAsync(_deadLetterQueueName, false, deadLetterConsumer, cancellationToken);

            foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
                await _consumerChannel.QueueBindAsync(
                    _queueName,
                    _exchangeName,
                    eventName, cancellationToken: cancellationToken);
            
            foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
                await _consumerChannel.QueueBindAsync(
                    _deadLetterQueueName,
                    _deadLetterExchangeName,
                    eventName, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while starting the RabbitMQ connection.");
            Environment.FailFast("Critical error in StartAsync", ex);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Stopping RabbitMQ connection and consumer channel");
        }
        
        if (_consumerChannel is not null)
        {
            await _consumerChannel.CloseAsync(cancellationToken);
        }
    }

    public async Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var routingKey = @event.GetType().Name;

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id,
                            routingKey);
        }
            

        if (_rabbitMQConnection is null)
        {
            throw new ApplicationException("RabbitMQ connection is not initialized");
        }

        await using var channel = await _rabbitMQConnection.CreateChannelAsync(cancellationToken: cancellationToken) ??
                            throw new ApplicationException("RabbitMQ connection is not initialized");

        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

        await channel.ExchangeDeclareAsync(_exchangeName, "direct", cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(_deadLetterExchangeName, "direct", cancellationToken: cancellationToken);

        var body = SerializationUtils.SerializeMessage(@event, _subscriptionInfo);

        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        var activityName = $"{routingKey} publish";

        // We call the helper method to perform the actual logic
        await ExecutePublishAsync(channel, routingKey, body, @event, activityName, cancellationToken);
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
            if (Activity.Current != null)
            {
                contextToInject = Activity.Current.Context;
            }

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
        if (_consumerChannel is null)
        { 
            throw new ApplicationException("Consumer channel is not initialized"); 
        }

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

        var success = await ProcessEvent(eventName, message);
        success = false;
        if (success)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("Successfully processed RabbitMQ event: {EventName}", eventName);

            await _consumerChannel.BasicAckAsync(eventArgs.DeliveryTag, false);
            return;
        }
        
        _logger.LogError("Failed to process RabbitMQ event: {EventName}. Requeuing.", eventName);
        await _consumerChannel.BasicRejectAsync(eventArgs.DeliveryTag, true);
    }
    
    private async Task OnDeadLetterMessageReceived(object sender, BasicDeliverEventArgs eventArgs)
    {
        if (_consumerChannel is null)
        {
            throw new ApplicationException("Consumer channel is not initialized");
        }

        // Extract the PropagationContext of the upstream parent from the message headers.
        var parentContext =
            _propagator.Extract(default, eventArgs.BasicProperties, ExtractTraceContextFromBasicProperties);
        Baggage.Current = parentContext.Baggage;

        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        var activityName = $"{eventArgs.RoutingKey} receive";

        using var activity =
            _activitySource.StartActivity(activityName, ActivityKind.Client, parentContext.ActivityContext);

        if (activity is null)
        {
            throw new ApplicationException("Activity is not initialized");
        }

        RabbitMQTelemetryHelpers.SetMessageActivityContext(activity, eventArgs.RoutingKey, "receive");

        var eventName = eventArgs.RoutingKey;

        if (!_subscriptionInfo.DeadLetterEventTypes.ContainsKey(eventName) && !_subscriptionInfo.IgnoreDeadLetterEventTypes.ContainsKey(eventName))
        {
            throw new ApplicationException($"Could not process dead letter message for EventName: {eventName}. Handler is not configured and ignore dead letter is not configured for this type.");
        }
        if (_subscriptionInfo.IgnoreDeadLetterEventTypes.ContainsKey(eventName))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Ignoring dead letter event: {EventName}. Event type is configured to be ignored.", eventName);
            }
            
            await _consumerChannel.BasicNackAsync(eventArgs.DeliveryTag, false, false);
            return;
        }

        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        var success = await ProcessDeadLetterEvent(eventName, message);

        if (success)
        {
            await _consumerChannel.BasicAckAsync(eventArgs.DeliveryTag, false);
        }
        else
        {
            _logger.LogError("Failed to process dead letter event: {EventName}.", eventName);
            throw new ApplicationException($"Failed to process dead letter event: {eventName}. Event type is not configured or is ignored.");   
        }
    }
    
    private static IEnumerable<string> ExtractTraceContextFromBasicProperties(IReadOnlyBasicProperties props, string key)
    {
        if (props.Headers == null || !props.Headers.TryGetValue(key, out var value))
            return [];

        if (value is byte[] bytes)
            return [Encoding.UTF8.GetString(bytes)];

        return [];
    }

    // Helper Methods
    // ==============================
    private async Task<bool> ProcessEvent(string eventName, string message)
    {
        if (_logger.IsEnabled(LogLevel.Trace)) { 
            _logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName); 
        }

        await using var scope = _serviceProvider.CreateAsyncScope();

        if (!_subscriptionInfo.EventTypes.TryGetValue(eventName, out var eventType))
        {
            _logger.LogError("Unable to resolve event type for event name {EventName}", eventName);
            return false;
        }

        try
        {
            var integrationEvent = SerializationUtils.DeserializeMessage(message, eventType, _subscriptionInfo);

            // REVIEW: This could be done in parallel
            foreach (var handler in scope.ServiceProvider.GetKeyedServices<IIntegrationEventHandler>(eventType))
                await handler.Handle(integrationEvent);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing event: {EventName}", eventName);
            return false;
        }
        
    }
    
    private async Task<bool> ProcessDeadLetterEvent(string eventName, string message)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName);
        }

        await using var scope = _serviceProvider.CreateAsyncScope();

        if (!_subscriptionInfo.DeadLetterEventTypes.TryGetValue(eventName, out var eventType))
        {
            _logger.LogWarning("Unable to resolve event type for event name {EventName}", eventName);
            throw new ApplicationException("");
        }

        var integrationEvent = SerializationUtils.DeserializeMessage(message, eventType, _subscriptionInfo);

        try
        {
            foreach (var handler in scope.ServiceProvider.GetKeyedServices<IIntegrationDeadLetterEventHandler>(
                         eventType))
                await handler.Handle(integrationEvent);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing dead letter event: {EventName}", eventName);
            return false;
        }
        
    }

    private static ResiliencePipeline CreateResiliencePipeline(int retryCount)
    {
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

    public void Dispose()
    {
        _consumerChannel?.Dispose();
    }
}
