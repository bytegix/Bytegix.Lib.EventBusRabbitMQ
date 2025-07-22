# Bytegix.Lib.EventBusRabbitMQ

## Overview
Bytegix.Lib.EventBusRabbitMQ is a .NET library designed to simplify RabbitMQ integration in your applications. It provides flexible configuration options, dependency injection extensions, and event handling abstractions for seamless message-based communication.

## Features
- **Flexible Configuration**: Supports connection strings, explicit parameters, and configuration sections from `appsettings.json`.
- **Event Handling**: Includes abstractions for creating and handling integration events.
- **Advanced Options**: Configure concurrency, exchange names, retry policies, and more.

## Installation
Install the package via NuGet:
```sh
dotnet add package Bytegix.Lib.EventBusRabbitMQ
```

## Example Usage
### Registering the Event Bus
```csharp
builder.AddRabbitMqEventBus("amqp://user:pass@host:5672/vhost", "MySubscriptionClient");
```

### Subscribing to Events
```csharp
builder.AddRabbitMqEventBus("amqp://user:pass@host:5672/vhost", "MySubscriptionClient")
    .AddSubscription<TestEvent, TestEventHandler>();
```

### Example Configuration in `appsettings.json`
```json
{
  "EventBus": {
    "HostName": "host",
    "UserName": "user",
    "Password": "pass",
    "VirtualHost": "/",
    "Port": 5672,
    "SubscriptionClientName": "MySubscriptionClient",
    "ConsumerDispatchConcurrency": 4,
    "ExchangeName": "CustomExchange",
    "RetryCount": 5
  }
}
```

## Development Status
This package is currently in development and may undergo significant changes. Contributions and feedback are welcome!

## Contributions and Inspirations
Inspired by Aspire.RabbitMQ.Client and the eShop Reference Application ([https://github.com/dotnet/eShop](https://github.com/dotnet/eShop)).