using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Bytegix.Lib.EventBusRabbitMQ.Telemetry;
internal sealed class RabbitMQEventSourceLogForwarder : IDisposable
{
    // ReSharper disable once InconsistentNaming
    private static readonly Func<ErrorEventSourceEvent, Exception?, string> s_formatErrorEvent = FormatErrorEvent;

    private readonly ILogger _logger;
    private RabbitMQEventSourceListener? _listener;

    public RabbitMQEventSourceLogForwarder(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("RabbitMQ.Client");
    }

    /// <summary>
    /// Initiates the log forwarding from the RabbitMQ event sources to a provided <see cref="ILoggerFactory"/>, call <see cref="Dispose"/> to stop forwarding.
    /// </summary>
    public void Start()
    {
        _listener ??= new RabbitMQEventSourceListener(LogEvent, EventLevel.Verbose);
    }

    private void LogEvent(EventWrittenEventArgs eventData)
    {
        var level = MapLevel(eventData.Level);
        var eventId = new EventId(eventData.EventId, eventData.EventName);

        // Special case the Error event so the Exception Details are written correctly
        if (eventData is { EventId: 3, EventName: "Error", PayloadNames: ["message", "ex"], Payload.Count: 2 })
        {
            _logger.Log(level, eventId, new ErrorEventSourceEvent(eventData), null, s_formatErrorEvent);
        }
        else
        {
            Debug.Assert(
                eventData is { EventId: 1, EventName: "Info" } ||
                eventData is { EventId: 2, EventName: "Warn" });

            _logger.Log(level, eventId, eventData.Payload?[0]?.ToString() ?? "<empty>");
        }
    }

    private static string FormatErrorEvent(ErrorEventSourceEvent eventSourceEvent, Exception? ex) =>
        eventSourceEvent.EventData.Payload?[0]?.ToString() ?? "<empty>";

    public void Dispose() => _listener?.Dispose();

    private static LogLevel MapLevel(EventLevel level) => level switch
    {
        EventLevel.Critical => LogLevel.Critical,
        EventLevel.Error => LogLevel.Error,
        EventLevel.Informational => LogLevel.Information,
        EventLevel.Verbose => LogLevel.Debug,
        EventLevel.Warning => LogLevel.Warning,
        EventLevel.LogAlways => LogLevel.Information,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    private readonly struct ErrorEventSourceEvent : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public EventWrittenEventArgs EventData { get; }

        public int Count { get; }

        public ErrorEventSourceEvent(EventWrittenEventArgs eventData)
        {
            EventData = eventData;

            var exData = eventData!.Payload![1] as IDictionary<string, object?>;
            Count = string.IsNullOrEmpty(exData!["InnerException"]?.ToString()) ? 3 : 4;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                Debug.Assert(EventData.PayloadNames?.Count == 2 && EventData.Payload?.Count == 2);
                Debug.Assert(EventData.PayloadNames[0] == "message");
                Debug.Assert(EventData.PayloadNames[1] == "ex");

                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

                var exData = EventData.Payload[1] as IDictionary<string, object?>;
                Debug.Assert(exData is not null && exData.Count == 4);

                return index switch
                {
                    0 => new KeyValuePair<string, object?>("exception.type", exData["Type"]),
                    1 => new KeyValuePair<string, object?>("exception.message", exData["Message"]),
                    2 => new KeyValuePair<string, object?>("exception.stacktrace", exData["StackTrace"]),
                    3 => new KeyValuePair<string, object?>("exception.innerexception", exData["InnerException"]),
                    _ => throw new UnreachableException()
                };
            }
        }
    }

    /// <summary>
    /// Implementation of <see cref="EventListener"/> that listens to events produced by the RabbitMQ.Client library.
    /// </summary>
    private sealed class RabbitMQEventSourceListener : EventListener
    {
        private readonly List<EventSource> _eventSources = new List<EventSource>();

        private readonly Action<EventWrittenEventArgs> _log;
        private readonly EventLevel _level;

        public RabbitMQEventSourceListener(Action<EventWrittenEventArgs> log, EventLevel level)
        {
            _log = log;
            _level = level;

            foreach (EventSource eventSource in _eventSources)
            {
                OnEventSourceCreated(eventSource);
            }

            _eventSources.Clear();
        }

        protected sealed override void OnEventSourceCreated(EventSource eventSource)
        {
            base.OnEventSourceCreated(eventSource);

            if (_log is null)
            {
                _eventSources.Add(eventSource);
            }

            if (eventSource.Name is "rabbitmq-dotnet-client" or "rabbitmq-client")
            {
                EnableEvents(eventSource, _level);
            }
        }

        protected sealed override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Workaround https://github.com/dotnet/corefx/issues/42600
            if (eventData.EventId == -1)
            {
                return;
            }

            // There is a very tight race during the listener creation where EnableEvents was called
            // and the thread producing events not observing the `_log` field assignment
            _log?.Invoke(eventData);
        }
    }
}