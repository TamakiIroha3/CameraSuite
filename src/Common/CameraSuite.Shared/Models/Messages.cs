using System.Text.Json.Serialization;

namespace CameraSuite.Shared.Models;

public sealed record AuthRequest(
    string SourceId,
    string ChannelName,
    string AuthCode,
    string ViewerAddress);

public sealed record AuthResponse(
    bool Accepted,
    string Message,
    string StreamKey,
    string SrtHost,
    int SrtPort,
    byte[]? AesKey,
    byte[]? AesIv,
    DateTimeOffset IssuedAt);

public sealed record StreamAnnouncement(
    string SourceId,
    string ChannelName,
    string DisplayName,
    string StreamKey,
    string SrtHost,
    int SrtPort,
    byte[]? AesKey,
    byte[]? AesIv,
    DateTimeOffset Timestamp);

public enum StreamLifecycle
{
    Starting = 0,
    Ready = 1,
    Stopped = 2,
    Failed = 3,
}

public sealed record StreamStateUpdate(
    string SourceId,
    string ChannelName,
    StreamLifecycle State,
    string? RecordingPath,
    string? ErrorMessage,
    DateTimeOffset Timestamp);

public enum SourceCommandType
{
    BeginStream = 0,
    StopStream = 1,
    ReloadConfiguration = 2,
}

public sealed record SourceCommand(
    SourceCommandType Command,
    string ChannelName,
    string? Payload,
    DateTimeOffset Timestamp);

public sealed record RecordingInfo(
    string ChannelName,
    string FilePath,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record ViewerTelemetry(
    string ChannelName,
    string Status,
    double? Fps,
    double? BitrateKbps,
    DateTimeOffset Timestamp);

public sealed record HealthPing(
    string Component,
    string InstanceId,
    string Status,
    DateTimeOffset Timestamp);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuthRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(StreamAnnouncement))]
[JsonSerializable(typeof(StreamStateUpdate))]
[JsonSerializable(typeof(SourceCommand))]
[JsonSerializable(typeof(RecordingInfo))]
[JsonSerializable(typeof(ViewerTelemetry))]
[JsonSerializable(typeof(HealthPing))]
public sealed partial class MessagingJsonContext : JsonSerializerContext;
