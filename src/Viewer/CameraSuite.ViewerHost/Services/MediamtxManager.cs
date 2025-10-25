using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CameraSuite.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraSuite.ViewerHost;

public sealed class MediamtxManager : IAsyncDisposable
{
    private readonly ViewerOptions _viewerOptions;
    private readonly RecordingOptions _recordingOptions;
    private readonly ILogger<MediamtxManager> _logger;
    private readonly Dictionary<string, MediamtxStreamConfig> _streams = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;

    public MediamtxManager(IOptions<CameraSuiteOptions> options, ILogger<MediamtxManager> logger)
    {
        var value = options.Value;
        _viewerOptions = value.Viewer;
        _recordingOptions = value.Recording;
        _logger = logger;
    }

    public async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        await WriteConfigAsync(cancellationToken).ConfigureAwait(false);
        StartProcess();
    }

    public async Task RegisterStreamAsync(StreamViewModel stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stream.Passphrase))
        {
            return;
        }

        _streams[stream.ChannelName] = new MediamtxStreamConfig(stream.ChannelName, stream.SrtPort, stream.Passphrase);
        await WriteConfigAsync(cancellationToken).ConfigureAwait(false);
        RestartProcess();
    }

    private async Task WriteConfigAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_viewerOptions.MediamtxConfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("logLevel: info");
        builder.AppendLine("readTimeout: 10s");
        builder.AppendLine("api: yes");
        builder.AppendLine("apiAddress: 127.0.0.1:9997");
        builder.AppendLine("paths:");

        if (_streams.Count == 0)
        {
            builder.AppendLine("  default:");
            builder.AppendLine("    source: srt://:6000?mode=listener");
            builder.AppendLine("    record: false");
        }
        else
        {
            foreach (var stream in _streams.Values)
            {
                builder.AppendLine($"  {stream.Channel}:");
                builder.AppendLine($"    source: srt://:{stream.Port}?mode=listener");
                builder.AppendLine($"    srtPassphrase: \"{stream.Passphrase}\"");
                builder.AppendLine("    srtPbKeyLen: 32");
                builder.AppendLine("    record: true");
                var recordPath = Path.Combine(_recordingOptions.RootDirectory, stream.Channel, "%Y%m%d", "%H%M%S.ts");
                builder.AppendLine($"    recordPath: {recordPath.Replace('\\', '/')}");
                builder.AppendLine("    recordFormat: mpegts");
            }
        }

        await File.WriteAllTextAsync(_viewerOptions.MediamtxConfigPath, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private void StartProcess()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _viewerOptions.MediamtxExecutable,
            Arguments = $"-c \"{_viewerOptions.MediamtxConfigPath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            CreateNoWindow = true,
        };

        try
        {
            _process = Process.Start(startInfo);
            if (_process == null)
            {
                throw new InvalidOperationException("mediamtx process could not be started");
            }

            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => _logger.LogWarning("mediamtx process exited");
            _ = Task.Run(() => PumpLogsAsync(_process.StandardError));
            _logger.LogInformation("mediamtx started with pid {Pid}", _process.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start mediamtx");
        }
    }

    private async Task PumpLogsAsync(StreamReader reader)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                _logger.LogDebug("mediamtx: {Line}", line);
            }
        }
        catch
        {
        }
    }

    private void RestartProcess()
    {
        StopProcess();
        StartProcess();
    }

    private void StopProcess()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                _process.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop mediamtx");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopProcess();
        await Task.CompletedTask;
    }

    private sealed record MediamtxStreamConfig(string Channel, int Port, string Passphrase);
}
