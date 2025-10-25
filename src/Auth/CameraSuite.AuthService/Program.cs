using CameraSuite.AuthService;
using CameraSuite.Shared.Extensions;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddCameraSuiteCore(builder.Configuration);
builder.Services.AddSingleton<AuthState>();
builder.Services.AddHostedService<AuthConsoleBootstrapper>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
