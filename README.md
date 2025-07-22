# Bytegix.Lib.EventBusRabbitMQ

[![publish](https://github.com/bytegix/Bytegix.Lib.EventBusRabbitMQ/actions/workflows/publish.yml/badge.svg)](https://github.com/bytegix/Bytegix.Lib.EventBusRabbitMQ/actions/workflows/publish.yml)
[![tests](https://github.com/bytegix/Bytegix.Lib.EventBusRabbitMQ/actions/workflows/test.yml/badge.svg)](https://github.com/bytegix/Bytegix.Lib.EventBusRabbitMQ/actions/workflows/test.yml)

# README: Using the RabbitMQ Event Bus Library

This library provides a robust implementation for integrating RabbitMQ into your .NET applications. It includes dependency injection extensions, configuration options, and event handling abstractions to simplify message-based communication.

---

# Development Status

This package is currently in development. While it provides a robust implementation for integrating RabbitMQ into .NET applications, it is not yet feature-complete and may undergo significant changes. Contributions and feedback are welcome to help improve the library.

---

## Installation

Add the library to your project via NuGet:

```sh
dotnet add package Bytegix.Lib.EventBusRabbitMQ
```

---

## Configuration Overview

The library provides multiple ways to configure and register the RabbitMQ Event Bus. You can use connection strings, explicit parameters, or configuration sections from your `appsettings.json`.

---

## Registration Methods

### 1. **Using Connection String**
You can register the RabbitMQ Event Bus using a connection string and a subscription client name.

```csharp
builder.AddRabbitMqEventBus("amqp://user:pass@host:5672/vhost", "MySubscriptionClient");
```

### 2. **Using Connection String with Custom Configuration**
You can provide additional configuration using an `Action<RabbitMQConfiguration>`.

```csharp
builder.AddRabbitMqEventBus(
    "amqp://user:pass@host:5672/vhost",
    "MySubscriptionClient",
    config =>
    {
        config.ConsumerDispatchConcurrency = 4;
        config.ExchangeName = "CustomExchange";
        config.RetryCount = 5;
    });
```

### 3. **Using Connection String with Predefined Configuration**
You can pass a predefined `RabbitMQConfiguration` object.

```csharp
var rabbitMqConfig = new RabbitMQConfiguration
{
    ConsumerDispatchConcurrency = 4,
    ExchangeName = "CustomExchange",
    RetryCount = 5
};

builder.AddRabbitMqEventBus("amqp://user:pass@host:5672/vhost", "MySubscriptionClient", rabbitMqConfig);
```

### 4. **Using Explicit Parameters**
You can register the RabbitMQ Event Bus using explicit parameters for host, username, password, port, and virtual host.

```csharp
builder.AddRabbitMqEventBus(
    host: "host",
    username: "user",
    password: "pass",
    subscriptionClientName: "MySubscriptionClient",
    port: 5672,
    vhost: "/");
```

### 5. **Using Explicit Parameters with Custom Configuration**
You can provide additional configuration using an `Action<RabbitMQConfiguration>`.

```csharp
builder.AddRabbitMqEventBus(
    host: "host",
    username: "user",
    password: "pass",
    subscriptionClientName: "MySubscriptionClient",
    port: 5672,
    vhost: "/",
    config =>
    {
        config.ConsumerDispatchConcurrency = 4;
        config.ExchangeName = "CustomExchange";
        config.RetryCount = 5;
    });
```

### 6. **Using Explicit Parameters with Predefined Configuration**
You can pass a predefined `RabbitMQConfiguration` object.

```csharp
var rabbitMqConfig = new RabbitMQConfiguration
{
    ConsumerDispatchConcurrency = 4,
    ExchangeName = "CustomExchange",
    RetryCount = 5
};

builder.AddRabbitMqEventBus(
    host: "host",
    username: "user",
    password: "pass",
    subscriptionClientName: "MySubscriptionClient",
    port: 5672,
    vhost: "/",
    rabbitMqConfig);
```

### 7. **Using Configuration Section**
You can register the RabbitMQ Event Bus using a configuration section from `appsettings.json`.

```csharp
builder.AddRabbitMqEventBus("EventBus");
```

Example `appsettings.json`:
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

---

## Event Handling

### Example Event
The library uses `IntegrationEvent` as the base class for events. Here's an example event:

```csharp
public record TestEvent : IntegrationEvent
{
    public string? TestProperty { get; set; }
}
```

### Example Event Handler
Event handlers implement `IIntegrationEventHandler<TIntegrationEvent>`. Here's an example handler:

```csharp
public class TestEventHandler : IIntegrationEventHandler<TestEvent>
{
    public async Task Handle(TestEvent @event)
    {
        Console.WriteLine($"TestEvent received with TestProperty: {@event.TestProperty}");
        ... // Handle the event logic here
    }
}
```

### Subscribing to Events
You can subscribe to events using the `AddSubscription` method.

```csharp
builder.AddRabbitMqEventBus("amqp://user:pass@host:5672/vhost", "MySubscriptionClient")
    .AddSubscription<TestEvent, TestEventHandler>();
```

---

Here is the updated **Advanced Configuration** section with all examples:

---

## Advanced Configuration

### RabbitMQConfiguration
The `RabbitMQConfiguration` class exposes advanced configuration options:

- `ConsumerDispatchConcurrency`: Number of concurrent consumers.
- `MaximumInboundMessageSize`: Maximum size of inbound messages.
- `ExchangeName`: Name of the RabbitMQ exchange.
- `RetryCount`: Number of retry attempts for failed operations.
- `DisableHealthChecks`: Disable RabbitMQ health checks.
- `DisableTracing`: Disable OpenTelemetry tracing.

Example:
```csharp
var config = new RabbitMQConfiguration
{
    ConsumerDispatchConcurrency = 4,
    MaximumInboundMessageSize = 1024 * 1024, // 1 MB
    ExchangeName = "CustomExchange",
    RetryCount = 5,
    DisableHealthChecks = false,
    DisableTracing = false
};
```

---

### Registration Methods with Advanced Configuration

#### 1. **Using Connection String with Custom Configuration**
You can provide additional configuration using an `Action<RabbitMQConfiguration>`.

```csharp
builder.AddRabbitMqEventBus(
    "amqp://user:pass@host:5672/vhost",
    "MySubscriptionClient",
    config =>
    {
        config.ConsumerDispatchConcurrency = 4;
        config.ExchangeName = "CustomExchange";
        config.RetryCount = 5;
    });
```

#### 2. **Using Connection String with Predefined Configuration**
You can pass a predefined `RabbitMQConfiguration` object.

```csharp
var rabbitMqConfig = new RabbitMQConfiguration
{
    ConsumerDispatchConcurrency = 4,
    ExchangeName = "CustomExchange",
    RetryCount = 5
};

builder.AddRabbitMqEventBus("amqp://user:pass@host:5672/vhost", "MySubscriptionClient", rabbitMqConfig);
```

#### 3. **Using Explicit Parameters with Custom Configuration**
You can provide additional configuration using an `Action<RabbitMQConfiguration>`.

```csharp
builder.AddRabbitMqEventBus(
    host: "host",
    username: "user",
    password: "pass",
    subscriptionClientName: "MySubscriptionClient",
    port: 5672,
    vhost: "/",
    config =>
    {
        config.ConsumerDispatchConcurrency = 4;
        config.ExchangeName = "CustomExchange";
        config.RetryCount = 5;
    });
```

#### 4. **Using Explicit Parameters with Predefined Configuration**
You can pass a predefined `RabbitMQConfiguration` object.

```csharp
var rabbitMqConfig = new RabbitMQConfiguration
{
    ConsumerDispatchConcurrency = 4,
    ExchangeName = "CustomExchange",
    RetryCount = 5
};

builder.AddRabbitMqEventBus(
    host: "host",
    username: "user",
    password: "pass",
    subscriptionClientName: "MySubscriptionClient",
    port: 5672,
    vhost: "/",
    rabbitMqConfig);
```

---

### Using Configuration Section
You can register the RabbitMQ Event Bus using a configuration section from `appsettings.json`.

```csharp
builder.AddRabbitMqEventBus("EventBus");
```

Example `appsettings.json`:
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

This section now comprehensively covers all advanced configuration methods with examples.

---

# Contributions and Inspirations

This package is inspired by the Aspire.RabbitMQ.Client package and the eShop Reference Application ([https://github.com/dotnet/eShop](https://github.com/dotnet/eShop)). These projects have provided valuable insights and patterns for implementing RabbitMQ-based event bus systems.

---

# Summary

This library simplifies RabbitMQ integration in .NET applications by providing flexible configuration options, dependency injection extensions, and event handling abstractions. Use the examples above to configure and use the library in your project.