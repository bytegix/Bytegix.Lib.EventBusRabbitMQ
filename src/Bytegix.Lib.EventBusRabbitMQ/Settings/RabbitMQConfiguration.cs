namespace Bytegix.Lib.EventBusRabbitMQ.Settings;
public class RabbitMQConfiguration
{
    // Properties
    // ==============================
    public ushort ConsumerDispatchConcurrency { get; set; } = 1;
    public int MaximumInboundMessageSize { get; set; } = 512 * 1024 * 1024;
    public string ExchangeName { get; set; } = "Default";
    public int RetryCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the RabbitMQ health check is disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the OpenTelemetry tracing is disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableTracing { get; set; }
}
