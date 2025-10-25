using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CameraSuite.SourceService;

public sealed class FfmpegProcessManager : IAsyncDisposable
{
    private readonly ILogger<FfmpegProcessManager> _logger;
    private Process? _process;
    private Task? _stderrPump;

    public FfmpegProcessManager(ILogger<FfmpegProcessManager> logger)
    {
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(
        StreamSession session,
        string srtPassphrase,
        Func<Task>? onStarted,
        CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(session, srtPassphrase);
        _logger.LogInformation("启动 FFmpeg 推流，参数 {Arguments}", arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = session.FfmpegExecutable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            CreateNoWindow = true,
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 FFmpeg 进程");
        _process.EnableRaisingEvents = true;

        _stderrPump = Task.Run(() => PumpStdErrAsync(_process, cancellationToken), CancellationToken.None);

        if (onStarted != null)
        {
            await onStarted().ConfigureAwait(false);
        }

        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("FFmpeg 退出，代码 {ExitCode}", _process.ExitCode);

        return _process.ExitCode;
    }

    private async Task PumpStdErrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            var reader = process.StandardError;
            string? line;
            while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                _logger.LogDebug("[ffmpeg] {Line}", line);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取 FFmpeg 日志时出错");
        }
    }

    private static string BuildArguments(StreamSession session, string passphrase)
    {
        var srtUrl = $"srt://{session.SrtHost}:{session.SrtPort}?mode=caller&streamid={session.StreamKey}";
        var builder = new StringBuilder();

        builder.Append("-re ");
        builder.Append("-i ");
        builder.Append(EscapeArgument($"{session.LocalRtmpUrl}/{session.ChannelName}"));
        builder.Append(' ');
        builder.Append("-c copy ");
        builder.Append("-f mpegts ");
        builder.Append("-flush_packets 0 ");
        builder.Append($"-srt_passphrase {EscapeArgument(passphrase)} ");
        builder.Append("-srt_pbkeylen 32 ");
        builder.Append(EscapeArgument(srtUrl, quote: true));

        return builder.ToString();
    }

    private static string EscapeArgument(string value, bool quote = false)
    {
        var escaped = value.Replace("\"", "\\\"");
        return quote ? $"\"{escaped}\"" : escaped;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(true);
            }
            catch
            {
                // ignore
            }
        }

        if (_stderrPump is not null)
        {
            try
            {
                await _stderrPump.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _process?.Dispose();
    }
}
