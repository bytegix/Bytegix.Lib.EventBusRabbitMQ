using OpenTelemetry.Context.Propagation;
using System.Diagnostics;

namespace Bytegix.Lib.EventBusRabbitMQ.Telemetry;

public class RabbitMQTelemetry
{
    // Constants
    // ==============================
    public const string ActivitySourceName = "EventBusRabbitMQ";

    // Properties
    // ==============================
    public ActivitySource ActivitySource { get; } = new(ActivitySourceName);
    public TextMapPropagator Propagator { get; } = Propagators.DefaultTextMapPropagator;
}
