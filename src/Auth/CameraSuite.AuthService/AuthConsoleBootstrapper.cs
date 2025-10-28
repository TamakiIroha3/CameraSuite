using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraSuite.AuthService;

public sealed class AuthConsoleBootstrapper : IHostedService
{
    private readonly AuthState _state;
    private readonly ILogger<AuthConsoleBootstrapper> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public AuthConsoleBootstrapper(AuthState state, ILogger<AuthConsoleBootstrapper> logger, IHostApplicationLifetime lifetime)
    {
        _state = state;
        _logger = logger;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Task.Run(() => RunInteractiveSetupAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task RunInteractiveSetupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var authCode = _state.AuthCode;

            Console.WriteLine("=============================================");
            Console.WriteLine(" CameraSuite Authentication Service (Auth)   ");
            Console.WriteLine("=============================================");
            Console.WriteLine("请在使用前确认已阅读并获取最新版本：.NET 8、MediaMTX、LibVLCSharp、SRT、FFmpeg 官方文档。");
            Console.WriteLine($"本次运行的认证码: {authCode}");
            Console.WriteLine("请将该认证码提供给影像源端。");
            Console.WriteLine();

            var viewerHost = Prompt("请输入观看端 (Viewer) 的主机地址或 IP");
            var apiPortInput = Prompt("请输入 mediamtx API 端口 (默认 9997，可直接回车)");
            var displayName = Prompt("请输入观看端展示名称 (可选)", optional: true);

            if (!int.TryParse(apiPortInput, out var apiPort) || apiPort <= 0)
            {
                apiPort = 9997;
            }

            if (!_state.TrySetViewerEndpoint(viewerHost, apiPort, displayName))
            {
                _logger.LogWarning("Viewer endpoint 已设置，忽略重复输入");
            }
            else
            {
                _logger.LogInformation("Viewer endpoint 已设置为 {Host}:{Port}", viewerHost, apiPort);
            }

            Console.WriteLine("等待影像源端通过 WebSocket 提交认证请求...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "交互式初始化失败，应用即将退出");
            _lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    private static string Prompt(string message, bool optional = false)
    {
        while (true)
        {
            Console.Write($"{message}{(optional ? " (可选)" : string.Empty)}: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                if (optional)
                {
                    return string.Empty;
                }

                continue;
            }

            return input.Trim();
        }
    }
}
