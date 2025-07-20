using Bytegix.Lib.EventBus.Events;
using Bytegix.Lib.EventBus.Subscription;
using System.Text.Json;

namespace Bytegix.Lib.EventBusRabbitMQ.Utils;
internal class SerializationUtils
{
    public static byte[] SerializeMessage(object @event, EventBusSubscriptionInfo subscriptionInfo)
    {
        // Serialize the event to JSON using the provided options
        var json = JsonSerializer.Serialize(@event, @event.GetType(), subscriptionInfo.JsonSerializerSettings);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public static IntegrationEvent DeserializeMessage(string message, Type eventType, EventBusSubscriptionInfo subscriptionInfo)
    {
        try
        {
            var deserialized = JsonSerializer.Deserialize(message, eventType, subscriptionInfo.JsonSerializerSettings) as IntegrationEvent;

            if (deserialized == null)
            {
                throw new ApplicationException("An error occurred while deserializing the message. The deserialized message is null.");
            }

            return deserialized;
        }
        catch (System.Exception ex)
        {
            throw new Exception($"An error occurred while deserializing the message: {ex.Message}");
        }
    }
}
