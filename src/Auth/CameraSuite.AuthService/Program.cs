using CameraSuite.AuthService;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddCameraSuiteCore(context.Configuration);
        services.AddSingleton<AuthState>();
        services.AddSingleton<ControlPlaneCertificateFactory>();
        services.AddSingleton<ViewerConnectionManager>();
        services.AddSingleton<SourceWebSocketEndpoint>();
        services.AddHostedService<AuthConsoleBootstrapper>();
        services.AddHostedService<Worker>();
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.ConfigureKestrel((context, options) =>
        {
            var port = context.Configuration.GetValue<int?>("CameraSuite:Auth:ControlPort") ?? 5051;
            var useTls = context.Configuration.GetValue<bool?>("CameraSuite:Auth:UseTls") ?? false;

            options.ListenAnyIP(port, listenOptions =>
            {
                if (useTls)
                {
                    listenOptions.UseHttps(httpsOptions =>
                    {
                        var factory = listenOptions.ApplicationServices.GetRequiredService<ControlPlaneCertificateFactory>();
                        httpsOptions.ServerCertificate = factory.GetOrCreateCertificate();
                    });
                }
            });
        });

        webBuilder.Configure(app =>
        {
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30),
            });

            app.Map("/ws/viewer", viewerApp =>
            {
                viewerApp.Run(async context =>
                {
                    if (!context.WebSockets.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    var manager = context.RequestServices.GetRequiredService<ViewerConnectionManager>();
                    using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                    await manager.RunSessionAsync(socket, context.RequestAborted).ConfigureAwait(false);
                });
            });

            app.Map("/ws/source", sourceApp =>
            {
                sourceApp.Run(async context =>
                {
                    if (!context.WebSockets.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    var handler = context.RequestServices.GetRequiredService<SourceWebSocketEndpoint>();
                    using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                    await handler.HandleAsync(socket, context.RequestAborted).ConfigureAwait(false);
                });
            });
        });
    })
    .Build();

await host.RunAsync().ConfigureAwait(false);
