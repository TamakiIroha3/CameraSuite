using CameraSuite.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace CameraSuite.SourceService;

public sealed class SourceState
{
    private readonly TaskCompletionSource<SourceRegistration> _registrationTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SourceOptions _options;
    private StreamSession? _currentSession;

    public SourceState(IOptions<CameraSuiteOptions> options)
    {
        _options = options.Value.Source;
    }

    public string SourceId => _options.SourceId;

    public SourceOptions Options => _options;

    public bool TrySetRegistration(SourceRegistration registration)
        => _registrationTcs.TrySetResult(registration);

    public ValueTask<SourceRegistration> WaitForRegistrationAsync(CancellationToken cancellationToken)
        => new(_registrationTcs.Task.WaitAsync(cancellationToken));

    public void SetSession(StreamSession session) => _currentSession = session;

    public StreamSession? CurrentSession => _currentSession;
}

public sealed record SourceRegistration(
    string AuthHost,
    int AuthPort,
    string AuthCode,
    string ChannelName,
    string? ViewerAddress);

public sealed record StreamSession(
    string ChannelName,
    string StreamKey,
    string SrtHost,
    int SrtPort,
    byte[] AesKey,
    byte[] AesIv,
    string LocalRtmpUrl,
    string FfmpegExecutable);
