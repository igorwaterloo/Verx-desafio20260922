var builder = Host.CreateApplicationBuilder(args);

// Consumidores de eventos (MassTransit) são registrados na Fase 5.

var host = builder.Build();

host.Run();
