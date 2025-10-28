namespace CameraSuite.Shared.Configuration;

public sealed class CameraSuiteOptions
{
    public const string SectionName = "CameraSuite";

    public RecordingOptions Recording { get; set; } = new();

    public AuthOptions Auth { get; set; } = new();

    public ViewerOptions Viewer { get; set; } = new();

    public SourceOptions Source { get; set; } = new();
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

    public int ControlPort { get; set; } = 5051;

    public bool UseTls { get; set; } = false;

    public bool AutoGenerateCertificate { get; set; } = true;

    public string CertificateSubject { get; set; } = "CN=CameraSuiteAuth";

    public int CertificateValidityDays { get; set; } = 30;

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }
}

public sealed class ViewerOptions
{
    public string MediamtxExecutable { get; set; } = "mediamtx";

    public string MediamtxConfigPath { get; set; } = "infra/mediamtx/mediamtx.yaml";

    public int MediamtxApiPort { get; set; } = 9997;

    public string ViewerId { get; set; } = $"viewer-{Environment.MachineName}";

    public int MaxSimultaneousStreams { get; set; } = 16;

    public int PreallocatedSrtListeners { get; set; } = 16;

    public int UiRefreshRateHz { get; set; } = 30;

    public string DisplayMode { get; set; } = "Grid";

    public string ControlPlaneUri { get; set; } = "ws://127.0.0.1:5051/ws/viewer";

    public bool TrustAllCertificates { get; set; } = true;
}

public sealed class SourceOptions
{
    public string SourceId { get; set; } = $"source-{Environment.MachineName}";

    public string DefaultChannelName { get; set; } = "default";

    public string LocalRtmpUrl { get; set; } = "rtmp://127.0.0.1/live";

    public string FfmpegExecutable { get; set; } = "ffmpeg";

    public int RetryDelaySeconds { get; set; } = 5;

    public int MaxRetryCount { get; set; } = 10;

    public string ControlPlanePath { get; set; } = "/ws/source";

    public bool UseTls { get; set; } = false;

    public bool TrustAllCertificates { get; set; } = true;
}
