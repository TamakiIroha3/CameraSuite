using CameraSuite.Shared.Extensions;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace CameraSuite.ViewerHost;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        NativeConsole.Allocate();
        Core.Initialize();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(e.Args);

        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddCameraSuiteCore(builder.Configuration);
        builder.Services.AddSingleton<ViewerState>();
        builder.Services.AddSingleton<PlaybackService>();
        builder.Services.AddSingleton<MediamtxManager>();
        builder.Services.AddHostedService<ViewerWorker>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();

        var services = _host.Services;
        var viewerState = services.GetRequiredService<ViewerState>();
        viewerState.AttachDispatcher(Current.Dispatcher);

        services.GetRequiredService<MediamtxManager>().EnsureRunningAsync(CancellationToken.None).GetAwaiter().GetResult();

        var mainWindow = services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
