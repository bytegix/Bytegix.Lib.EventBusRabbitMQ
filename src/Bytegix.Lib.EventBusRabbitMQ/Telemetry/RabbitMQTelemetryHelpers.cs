using System.Diagnostics;

namespace Bytegix.Lib.EventBusRabbitMQ.Telemetry;
internal static class RabbitMQTelemetryHelpers
{
    public static void AddRabbitMQTags(Activity? activity, Uri address, string? operation = null)
    {
        if (activity is null)
        {
            return;
        }

        activity.AddTag("server.address", address.Host);
        activity.AddTag("server.port", address.Port);
        activity.AddTag("messaging.system", "rabbitmq");
        if (operation is not null)
        {
            activity.AddTag("messaging.operation", operation);
        }
    }

    public static void AddRabbitMQExceptionTags(Activity? connectAttemptActivity, Exception ex)
    {
        if (connectAttemptActivity is null)
        {
            return;
        }

        connectAttemptActivity.AddTag("exception.message", ex.Message);
        // Note that "exception.stacktrace" is the full exception detail, not just the StackTrace property.
        // See https://opentelemetry.io/docs/specs/semconv/attributes-registry/exception/
        // and https://github.com/open-telemetry/opentelemetry-specification/pull/697#discussion_r453662519
        connectAttemptActivity.AddTag("exception.stacktrace", ex.ToString());
        connectAttemptActivity.AddTag("exception.type", ex.GetType().FullName);
        connectAttemptActivity.SetStatus(ActivityStatusCode.Error);
    }

    public static void SetMessageActivityContext(Activity activity, string routingKey, string operation)
    {
        // These tags are added demonstrating the semantic conventions of the OpenTelemetry messaging specification
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.destination_kind", "queue");
        activity.SetTag("messaging.operation", operation);
        activity.SetTag("messaging.destination.name", routingKey);
        activity.SetTag("messaging.rabbitmq.routing_key", routingKey);
    }
}
