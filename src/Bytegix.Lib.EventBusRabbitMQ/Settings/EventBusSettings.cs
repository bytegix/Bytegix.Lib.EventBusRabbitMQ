namespace Bytegix.Lib.EventBusRabbitMQ.Settings;

public class EventBusSettings
{
    // Constants
    // ==============================
    private const string DefaultExchangeName = "Default";
    private const int DefaultPort = 5672;
    private const int DefaultMaximumInboundMessageSize = 512 * 1024 * 1024; // 512 MB
    private const int DefaultRetryCount = 10;
    private const int DefaultConsumerDispatchConcurrency = 4;
    private const int DefaultRequeueCount = 10;

    // Properties
    // ==============================
    public string? HostName { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public ushort ConsumerDispatchConcurrency { get; set; }
    public string VirtualHost { get; set; }
    public int Port { get; set; }
    public int MaximumInboundMessageSize { get; set; }
    public string? SubscriptionClientName { get; set; }
    public string ExchangeName { get; set; }
    /// <summary>
    /// How many times to retry connection to RabbitMQ before giving up.
    /// </summary>
    public int RetryCount { get; set; }
    /// <summary>
    /// How many times to requeue a message before sending it to the dead-letter queue.
    /// </summary>
    public int RequeueCount { get; set; } = DefaultRequeueCount;

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

    // Constructors
    // ==============================
    public EventBusSettings()
    {
        ExchangeName = DefaultExchangeName;
        VirtualHost = "/";
    }

    public EventBusSettings(string hostName, string userName, string password, string vhost, string subscriptionClientName, int port = DefaultPort, RabbitMQConfiguration? configuration = null)
    {
        HostName = hostName;
        UserName = userName;
        Password = password;
        VirtualHost = vhost;
        SubscriptionClientName = subscriptionClientName;
        Port = port;

        ConsumerDispatchConcurrency = configuration?.ConsumerDispatchConcurrency ?? DefaultConsumerDispatchConcurrency;
        MaximumInboundMessageSize = configuration?.MaximumInboundMessageSize ?? DefaultMaximumInboundMessageSize;
        ExchangeName = configuration?.ExchangeName ?? DefaultExchangeName;
        RetryCount = configuration?.RetryCount ?? DefaultRetryCount;
        DisableHealthChecks = configuration?.DisableHealthChecks ?? false;
        DisableTracing = configuration?.DisableTracing ?? false;
        RequeueCount = configuration?.RequeueCount ?? DefaultRequeueCount;
    }

    public EventBusSettings(string connectionString, string subscriptionClientName, RabbitMQConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionClientName);

        var parsed = ParseAmqpConnectionString(connectionString);
        UserName = parsed.Username;
        Password = parsed.Password;
        HostName = parsed.Host;
        Port = parsed.Port;
        VirtualHost = parsed.Vhost;
        SubscriptionClientName = subscriptionClientName;

        ConsumerDispatchConcurrency = configuration?.ConsumerDispatchConcurrency ?? DefaultConsumerDispatchConcurrency;
        MaximumInboundMessageSize = configuration?.MaximumInboundMessageSize ?? DefaultMaximumInboundMessageSize;
        ExchangeName = configuration?.ExchangeName ?? DefaultExchangeName;
        RetryCount = configuration?.RetryCount ?? DefaultRetryCount;
        DisableHealthChecks = configuration?.DisableHealthChecks ?? false;
        DisableTracing = configuration?.DisableTracing ?? false;
        RequeueCount = configuration?.RequeueCount ?? DefaultRequeueCount;
    }

    // Helpers
    // ==============================
    private static (string Username, string Password, string Host, int Port, string Vhost) ParseAmqpConnectionString(string connectionString)
    {
        var uri = new Uri(connectionString);

        // Username and password
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;

        // Host and port
        var host = string.IsNullOrEmpty(uri.Host) ? string.Empty : Uri.UnescapeDataString(uri.Host);
        var port = uri.IsDefaultPort ? 5672 : uri.Port;

        // Vhost: remove leading slash, decode, and handle empty/"/" as per AMQP spec
        string vhost;
        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            vhost = "/";
        }
        else
        {
            // Remove leading slash and decode
            vhost = Uri.UnescapeDataString(uri.AbsolutePath[1..]);
        }

        return (username, password, host, port, vhost);
    }
}
