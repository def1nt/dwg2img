using dwg2img;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options => options.ServiceName = "dwg2img");

builder.Logging.ClearProviders();
builder.Logging.AddEventLog(eventLogSettings => eventLogSettings.SourceName = "dwg2img");
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var host = builder.Build();
host.Run();
