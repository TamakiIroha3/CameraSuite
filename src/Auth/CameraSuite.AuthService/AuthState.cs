using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Security;
using Microsoft.Extensions.Options;

namespace CameraSuite.AuthService;

public sealed class AuthState
{
    private readonly object _syncRoot = new();
    private readonly Queue<int> _availablePorts = new();
    private readonly HashSet<int> _availablePortSet = new();
    private readonly Dictionary<int, DateTimeOffset> _portAllocations = new();
    private readonly Dictionary<int, string> _portKeys = new();
    private readonly Dictionary<int, AesKeyMaterial> _slotMaterials = new();
    private readonly Dictionary<string, StreamAssignment> _assignments = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _portRangeStart;
    private readonly int _portRangeEnd;
    private readonly TimeSpan _portHoldDuration;
    private readonly TaskCompletionSource<ViewerEndpoint> _viewerEndpointSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _slotsInitialized;

    public AuthState(IOptions<CameraSuiteOptions> options)
    {
        var authOptions = options.Value.Auth;
        if (authOptions.SrtPortRangeStart >= authOptions.SrtPortRangeEnd)
        {
            throw new ArgumentException("SRT port range is invalid.");
        }

        _portRangeStart = authOptions.SrtPortRangeStart;
        _portRangeEnd = authOptions.SrtPortRangeEnd;
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
            if (!_slotsInitialized)
            {
                assignment = null!;
                failureReason = "Viewer listeners not ready";
                return false;
            }

            while (_availablePorts.Count > 0)
            {
                var port = _availablePorts.Dequeue();
                _availablePortSet.Remove(port);

                if (!_slotMaterials.TryGetValue(port, out var material))
                {
                    continue;
                }

                var streamKey = GenerateStreamKey();

                assignment = new StreamAssignment(sourceId, channelName, port, material, streamKey, DateTimeOffset.UtcNow);
                _assignments[assignment.Key] = assignment;
                _portAllocations[port] = assignment.CreatedAt;
                _portKeys[port] = assignment.Key;
                failureReason = null;
                return true;
            }

            assignment = null!;
            failureReason = "No available SRT ports";
            return false;
        }
    }

    public void SetListenerSlots(IReadOnlyList<ListenerSlotInfo> slots)
    {
        lock (_syncRoot)
        {
            _slotMaterials.Clear();
            _availablePorts.Clear();
            _availablePortSet.Clear();

            foreach (var listener in slots)
            {
                if (listener.Port < _portRangeStart || listener.Port > _portRangeEnd)
                {
                    continue;
                }

                var material = new AesKeyMaterial(listener.AesKey.ToArray(), listener.AesIv.ToArray());
                _slotMaterials[listener.Port] = material;
            }

            var validPorts = new HashSet<int>(_slotMaterials.Keys);

            foreach (var port in validPorts)
            {
                if (!_portAllocations.ContainsKey(port))
                {
                    EnqueuePort(port);
                }
            }

            foreach (var assignment in _assignments.Values.ToArray())
            {
                if (validPorts.Contains(assignment.Port))
                {
                    continue;
                }

                _assignments.Remove(assignment.Key);
                _portAllocations.Remove(assignment.Port);
                _portKeys.Remove(assignment.Port);
            }

            _slotsInitialized = _slotMaterials.Count > 0;
        }
    }

    private void EnqueuePort(int port)
    {
        if (_availablePortSet.Add(port))
        {
            _availablePorts.Enqueue(port);
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
                if (_slotMaterials.ContainsKey(assignment.Port))
                {
                    EnqueuePort(assignment.Port);
                }
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
                if (_slotMaterials.ContainsKey(port))
                {
                    EnqueuePort(port);
                }
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
