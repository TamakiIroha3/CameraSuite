using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraSuite.SourceService;

public sealed class SourceConsoleBootstrapper : IHostedService
{
    private readonly SourceState _state;
    private readonly ILogger<SourceConsoleBootstrapper> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public SourceConsoleBootstrapper(SourceState state, ILogger<SourceConsoleBootstrapper> logger, IHostApplicationLifetime lifetime)
    {
        _state = state;
        _logger = logger;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Task.Run(() => RunInteractiveAsync(), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void RunInteractiveAsync()
    {
        try
        {
            Console.WriteLine("=============================================");
            Console.WriteLine(" CameraSuite Source Service                  ");
            Console.WriteLine("=============================================");
            Console.WriteLine("请确保已在本地准备好 RTMP 输入源 (默认 rtmp://127.0.0.1/live/<channel>)。");
            Console.WriteLine();

            var authHost = Prompt("请输入认证端 IP/主机名");
            var authPort = PromptInt("请输入认证端端口(默认 4222)", defaultValue: 4222);
            var authCode = Prompt("请输入认证码");
            var channelName = Prompt($"请输入通道名称 (默认 {_state.Options.DefaultChannelName})", allowEmpty: true);

            if (string.IsNullOrWhiteSpace(channelName))
            {
                channelName = _state.Options.DefaultChannelName;
            }

            var viewerAddress = Prompt("请输入观看端识别名 (可选)", allowEmpty: true);

            var registration = new SourceRegistration(authHost, authPort, authCode, channelName, viewerAddress);
            if (!_state.TrySetRegistration(registration))
            {
                _logger.LogWarning("注册信息已存在，忽略重复输入");
            }
            else
            {
                _logger.LogInformation("已设置通道 {Channel}", channelName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "初始化失败，应用即将退出");
            _lifetime.StopApplication();
        }
    }

    private static string Prompt(string text, bool allowEmpty = false)
    {
        while (true)
        {
            Console.Write($"{text}: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                if (allowEmpty)
                {
                    return string.Empty;
                }

                continue;
            }

            return input.Trim();
        }
    }

    private static int PromptInt(string text, int defaultValue)
    {
        while (true)
        {
            Console.Write($"{text}: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return defaultValue;
            }

            if (int.TryParse(input, out var value))
            {
                return value;
            }

            Console.WriteLine("请输入有效的整数。");
        }
    }
}
