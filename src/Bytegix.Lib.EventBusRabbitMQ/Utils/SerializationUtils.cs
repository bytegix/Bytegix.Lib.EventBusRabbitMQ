using Bytegix.Lib.EventBus.Abstractions;
using Bytegix.Lib.EventBus.Events;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Bytegix.Lib.EventBusRabbitMQ.Utils;

internal static class SerializationUtils
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed .NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    internal static IntegrationEvent DeserializeMessage(string message, Type eventType, EventBusSubscriptionInfo subscriptionInfo)
    {
        try
        {
            var deserialized = JsonSerializer.Deserialize(message, eventType, subscriptionInfo.JsonSerializerOptions) as IntegrationEvent;

            if (deserialized == null)
            {
                throw new ApplicationException("An error occurred while deserializing the message. The deserialized message is null.");
            }

            // Process the entire object and convert JsonElements to objects if needed
            // ProcessJsonElements(deserialized, subscriptionInfo.JsonSerializerOptions);

            return deserialized;
        }
        catch (System.Exception ex)
        {
            throw new Exception($"An error occurred while deserializing the message: {ex.Message}");
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed .NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    internal static byte[] SerializeMessage(IntegrationEvent @event, EventBusSubscriptionInfo subscriptionInfo)
    {
        return JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), subscriptionInfo.JsonSerializerOptions);
    }
}
