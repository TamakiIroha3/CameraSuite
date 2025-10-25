using System.Security.Cryptography;
using System.Linq;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Security;
using Microsoft.Extensions.Options;

namespace CameraSuite.AuthService;

public sealed class AuthState
{
    private readonly object _syncRoot = new();
    private readonly Queue<int> _availablePorts = new();
    private readonly Dictionary<int, DateTimeOffset> _portAllocations = new();
    private readonly Dictionary<int, string> _portKeys = new();
    private readonly Dictionary<string, StreamAssignment> _assignments = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _portHoldDuration;
    private readonly TaskCompletionSource<ViewerEndpoint> _viewerEndpointSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AuthState(IOptions<CameraSuiteOptions> options)
    {
        var authOptions = options.Value.Auth;
        if (authOptions.SrtPortRangeStart >= authOptions.SrtPortRangeEnd)
        {
            throw new ArgumentException("SRT port range is invalid.");
        }

        for (var port = authOptions.SrtPortRangeStart; port <= authOptions.SrtPortRangeEnd; port++)
        {
            _availablePorts.Enqueue(port);
        }

        _portHoldDuration = TimeSpan.FromSeconds(Math.Max(30, authOptions.PortHoldSeconds));
        AuthCode = GenerateAuthCode();
    }

    public string AuthCode { get; }

    public ViewerEndpoint? ViewerEndpoint => _viewerEndpointSource.Task.IsCompletedSuccessfully ? _viewerEndpointSource.Task.Result : null;

    public bool TrySetViewerEndpoint(string host, int apiPort, string? displayName)
    {
        var endpoint = new ViewerEndpoint(host, apiPort, string.IsNullOrWhiteSpace(displayName) ? host : displayName);
        return _viewerEndpointSource.TrySetResult(endpoint);
    }

    public ValueTask<ViewerEndpoint> WaitForViewerEndpointAsync(CancellationToken cancellationToken)
        => new(_viewerEndpointSource.Task.WaitAsync(cancellationToken));

    public bool IsValidCode(string code)
        => string.Equals(AuthCode, code, StringComparison.OrdinalIgnoreCase);

    public bool TryReserveStream(string sourceId, string channelName, out StreamAssignment assignment, out string? failureReason)
    {
        lock (_syncRoot)
        {
            if (_availablePorts.Count == 0)
            {
                assignment = null!;
                failureReason = "No available SRT ports";
                return false;
            }

            var port = _availablePorts.Dequeue();
            var keyMaterial = AesKeyMaterial.Create();
            var streamKey = GenerateStreamKey();

            assignment = new StreamAssignment(sourceId, channelName, port, keyMaterial, streamKey, DateTimeOffset.UtcNow);
            _assignments[assignment.Key] = assignment;
            _portAllocations[port] = assignment.CreatedAt;
            _portKeys[port] = assignment.Key;
            failureReason = null;
            return true;
        }
    }

    public bool TryGetAssignment(string sourceId, string channelName, out StreamAssignment assignment)
    {
        lock (_syncRoot)
        {
            return _assignments.TryGetValue(StreamAssignment.MakeKey(sourceId, channelName), out assignment!);
        }
    }

    public void ReleaseStream(string sourceId, string channelName)
    {
        lock (_syncRoot)
        {
            var key = StreamAssignment.MakeKey(sourceId, channelName);
            if (_assignments.Remove(key, out var assignment))
            {
                _portAllocations.Remove(assignment.Port);
                _portKeys.Remove(assignment.Port);
                _availablePorts.Enqueue(assignment.Port);
            }
        }
    }

    public IReadOnlyList<StreamAssignment> CleanupExpired(DateTimeOffset now)
    {
        var expired = new List<StreamAssignment>();

        lock (_syncRoot)
        {
            foreach (var (port, issuedAt) in _portAllocations.ToArray())
            {
                if (now - issuedAt <= _portHoldDuration)
                {
                    continue;
                }

                if (_portKeys.TryGetValue(port, out var key) && _assignments.Remove(key, out var assignment))
                {
                    expired.Add(assignment);
                }

                _portAllocations.Remove(port);
                _portKeys.Remove(port);
                _availablePorts.Enqueue(port);
            }
        }

        return expired;
    }

    private static string GenerateAuthCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buffer = stackalloc char[8];
        using var random = RandomNumberGenerator.Create();
        Span<byte> bytes = stackalloc byte[buffer.Length];
        random.GetBytes(bytes);
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(buffer);
    }

    private static string GenerateStreamKey()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
}

public sealed record ViewerEndpoint(string Host, int MediamtxApiPort, string DisplayName)
{
    public override string ToString() => $"{DisplayName} ({Host}:{MediamtxApiPort})";
}

public sealed record StreamAssignment(
    string SourceId,
    string ChannelName,
    int Port,
    AesKeyMaterial KeyMaterial,
    string StreamKey,
    DateTimeOffset CreatedAt)
{
    public string Key => MakeKey(SourceId, ChannelName);

    public static string MakeKey(string sourceId, string channelName)
        => $"{sourceId.Trim()}::{channelName.Trim()}";
}
