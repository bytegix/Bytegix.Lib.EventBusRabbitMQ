namespace Bytegix.Lib.EventBusRabbitMQ.Settings;
public class EventBusSettings
{
    // Properties
    // ==============================
    public required string HostName { get; set; }
    public required string UserName { get; set; }
    public required string Password { get; set; }
    public ushort ConsumerDispatchConcurrency { get; set; } = 1;
    public string VirtualHost { get; set; } = "/";
    public int Port { get; set; } = 5672;
    public int MaximumInboundMessageSize { get; set; } = 512 * 1024 * 1024;
    public string? SubscriptionClientName { get; set; }
    public int RetryCount { get; set; } = 10;

    // Constructors
    // ==============================
    public EventBusSettings() { }
    public EventBusSettings(string hostName, string userName, string password)
    {
        HostName = hostName;
        UserName = userName;
        Password = password;
    }
}
