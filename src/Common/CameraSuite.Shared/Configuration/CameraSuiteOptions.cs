namespace CameraSuite.Shared.Configuration;

public sealed class CameraSuiteOptions
{
    public const string SectionName = "CameraSuite";

    public NatsOptions Nats { get; set; } = new();

    public RecordingOptions Recording { get; set; } = new();

    public AuthOptions Auth { get; set; } = new();

    public ViewerOptions Viewer { get; set; } = new();

    public SourceOptions Source { get; set; } = new();
}

public sealed class NatsOptions
{
    public string Url { get; set; } = "tls://127.0.0.1:4222";

    public string ClientName { get; set; } = $"camera-suite-{Environment.MachineName}";

    public string? ClientCertificatePath { get; set; }

    public string? ClientKeyPath { get; set; }

    public string? ClientKeyPassword { get; set; }

    public string? CaCertificatePath { get; set; }

    public bool TrustAllCertificates { get; set; } = true;

    public int RequestTimeoutSeconds { get; set; } = 5;
}

public sealed class RecordingOptions
{
    public string RootDirectory { get; set; } = "recordings";

    public bool AutoStartRecording { get; set; } = false;

    public int SegmentMinutes { get; set; } = 30;
}

public sealed class AuthOptions
{
    public int SrtPortRangeStart { get; set; } = 6000;

    public int SrtPortRangeEnd { get; set; } = 6999;

    public int PortHoldSeconds { get; set; } = 600;

    public int CleanupSweepSeconds { get; set; } = 60;
}

public sealed class ViewerOptions
{
    public string MediamtxExecutable { get; set; } = "mediamtx";

    public string MediamtxConfigPath { get; set; } = "infra/mediamtx/mediamtx.yaml";

    public string ViewerId { get; set; } = $"viewer-{Environment.MachineName}";

    public int MaxSimultaneousStreams { get; set; } = 16;

    public int UiRefreshRateHz { get; set; } = 30;

    public string DisplayMode { get; set; } = "Grid";
}

public sealed class SourceOptions
{
    public string SourceId { get; set; } = $"source-{Environment.MachineName}";

    public string DefaultChannelName { get; set; } = "default";

    public string LocalRtmpUrl { get; set; } = "rtmp://127.0.0.1/live";

    public string FfmpegExecutable { get; set; } = "ffmpeg";

    public int RetryDelaySeconds { get; set; } = 5;

    public int MaxRetryCount { get; set; } = 10;
}
