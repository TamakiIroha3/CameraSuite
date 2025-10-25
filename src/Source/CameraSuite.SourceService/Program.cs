using CameraSuite.SourceService;
using CameraSuite.Shared.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddCameraSuiteCore(builder.Configuration);
builder.Services.AddSingleton<SourceState>();
builder.Services.AddSingleton<FfmpegProcessManager>();
builder.Services.AddHostedService<SourceConsoleBootstrapper>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
