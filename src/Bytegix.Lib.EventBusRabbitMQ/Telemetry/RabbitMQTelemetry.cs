using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

namespace Bytegix.Lib.EventBusRabbitMQ.Telemetry;
public class RabbitMQTelemetry
{
    // Properties
    // ==============================
    public ActivitySource ActivitySource { get; } = new(ActivitySourceName);
    public TextMapPropagator Propagator { get; } = Propagators.DefaultTextMapPropagator;

    // Constants
    // ==============================
    public const string ActivitySourceName = "EventBusRabbitMQ";
}
