using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraSuite.ViewerHost;

public sealed class MediamtxManager : IAsyncDisposable
{
    private readonly ViewerOptions _viewerOptions;
    private readonly RecordingOptions _recordingOptions;
    private readonly AuthOptions _authOptions;
    private readonly ILogger<MediamtxManager> _logger;
    private readonly List<MediamtxListenerSlot> _slots = new();
    private readonly Dictionary<string, int> _activeStreams = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;
    private bool _initialized;

    public MediamtxManager(IOptions<CameraSuiteOptions> options, ILogger<MediamtxManager> logger)
    {
        var value = options.Value;
        _viewerOptions = value.Viewer;
        _recordingOptions = value.Recording;
        _authOptions = value.Auth;
        _logger = logger;
    }

    public IReadOnlyList<ListenerSlotInfo> GetSlotSnapshot()
    {
        lock (_slots)
        {
            return _slots
                .Select(slot => new ListenerSlotInfo(
                    slot.Port,
                    slot.KeyMaterial.Key.ToArray(),
                    slot.KeyMaterial.Iv.ToArray()))
                .ToArray();
        }
    }

    public async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            InitializeSlots();
            await WriteConfigAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }

        StartProcess();
    }

    public Task RegisterStreamAsync(StreamViewModel stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stream.Passphrase))
        {
            return Task.CompletedTask;
        }

        lock (_slots)
        {
            var slot = _slots.FirstOrDefault(s => s.Port == stream.SrtPort);
            if (slot is null)
            {
                _logger.LogWarning("No preallocated listener slot for channel {Channel} on port {Port}", stream.ChannelName, stream.SrtPort);
                return Task.CompletedTask;
            }

            if (!string.Equals(slot.Passphrase, stream.Passphrase, StringComparison.Ordinal))
            {
                _logger.LogWarning("Passphrase mismatch for port {Port}. Expected {Expected} but received {Actual}", stream.SrtPort, slot.Passphrase, stream.Passphrase);
            }

            _activeStreams[stream.ChannelName] = slot.Port;
        }

        return Task.CompletedTask;
    }

    private void InitializeSlots()
    {
        lock (_slots)
        {
            if (_slots.Count > 0)
            {
                return;
            }

            var availablePorts = Math.Max(0, _authOptions.SrtPortRangeEnd - _authOptions.SrtPortRangeStart + 1);
            if (availablePorts == 0)
            {
                throw new InvalidOperationException("SRT port range is empty. Please adjust Auth.SrtPortRangeStart/End.");
            }

            var desired = Math.Clamp(_viewerOptions.PreallocatedSrtListeners, 1, availablePorts);

            for (var i = 0; i < desired; i++)
            {
                var port = _authOptions.SrtPortRangeStart + i;
                _slots.Add(new MediamtxListenerSlot(port, AesKeyMaterial.Create()));
            }
        }
    }

    private async Task WriteConfigAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_viewerOptions.MediamtxConfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string BuildPaths()
        {
            var builder = new StringBuilder();
            builder.AppendLine("paths:");
            foreach (var slot in _slots)
            {
                builder.AppendLine($"  slot_{slot.Port}:");
                builder.AppendLine($"    source: srt://:{slot.Port}?mode=listener");
                builder.AppendLine($"    srtPassphrase: \"{slot.Passphrase}\"");
                builder.AppendLine("    srtPbKeyLen: 32");
                builder.AppendLine("    record: true");
                var recordPath = Path.Combine(_recordingOptions.RootDirectory, $"port-{slot.Port}", "%Y%m%d", "%H%M%S.ts");
                builder.AppendLine($"    recordPath: {recordPath.Replace('\\', '/')}");
                builder.AppendLine("    recordFormat: mpegts");
            }

            return builder.ToString();
        }

        var configBuilder = new StringBuilder();
        configBuilder.AppendLine("logLevel: info");
        configBuilder.AppendLine("readTimeout: 10s");
        configBuilder.AppendLine("api: yes");
        configBuilder.AppendLine($"apiAddress: 127.0.0.1:{_viewerOptions.MediamtxApiPort}");
        configBuilder.Append(BuildPaths());

        await File.WriteAllTextAsync(_viewerOptions.MediamtxConfigPath, configBuilder.ToString(), cancellationToken).ConfigureAwait(false);
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

    private sealed record MediamtxListenerSlot(int Port, AesKeyMaterial KeyMaterial)
    {
        public string Passphrase => KeyMaterial.ToPassphrase();
    }
}
