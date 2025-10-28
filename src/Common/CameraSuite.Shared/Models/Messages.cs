using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CameraSuite.Shared.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ViewerHello), typeDiscriminator: "viewer_hello")]
[JsonDerivedType(typeof(ViewerHelloAck), typeDiscriminator: "viewer_hello_ack")]
[JsonDerivedType(typeof(ErrorNotification), typeDiscriminator: "error")]
[JsonDerivedType(typeof(AuthRequest), typeDiscriminator: "auth_request")]
[JsonDerivedType(typeof(AuthResponse), typeDiscriminator: "auth_response")]
[JsonDerivedType(typeof(StreamAnnouncement), typeDiscriminator: "stream_announce")]
[JsonDerivedType(typeof(StreamStateUpdate), typeDiscriminator: "stream_state")]
public abstract record ControlMessage;

public sealed record ViewerHello(
    string ViewerId,
    string Host,
    int MediamtxApiPort,
    string? DisplayName,
    bool UseTls,
    bool TrustAllCertificates,
    IReadOnlyList<ListenerSlotInfo> Slots) : ControlMessage;

public sealed record ViewerHelloAck(
    bool Accepted,
    string AuthCode,
    string? Message) : ControlMessage;

public sealed record ErrorNotification(
    string Message,
    string? Detail) : ControlMessage;

public sealed record AuthRequest(
    string SourceId,
    string ChannelName,
    string AuthCode,
    string ViewerAddress) : ControlMessage;

public sealed record AuthResponse(
    bool Accepted,
    string Message,
    string StreamKey,
    string SrtHost,
    int SrtPort,
    byte[]? AesKey,
    byte[]? AesIv,
    DateTimeOffset IssuedAt) : ControlMessage;

public sealed record StreamAnnouncement(
    string SourceId,
    string ChannelName,
    string DisplayName,
    string StreamKey,
    string SrtHost,
    int SrtPort,
    byte[]? AesKey,
    byte[]? AesIv,
    DateTimeOffset Timestamp) : ControlMessage;

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
    DateTimeOffset Timestamp) : ControlMessage;

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

public sealed record ListenerSlotInfo(
    int Port,
    byte[] AesKey,
    byte[] AesIv);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ControlMessage))]
[JsonSerializable(typeof(ViewerHello))]
[JsonSerializable(typeof(ViewerHelloAck))]
[JsonSerializable(typeof(ErrorNotification))]
[JsonSerializable(typeof(AuthRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(StreamAnnouncement))]
[JsonSerializable(typeof(StreamStateUpdate))]
[JsonSerializable(typeof(ListenerSlotInfo))]
[JsonSerializable(typeof(SourceCommand))]
[JsonSerializable(typeof(RecordingInfo))]
[JsonSerializable(typeof(ViewerTelemetry))]
[JsonSerializable(typeof(HealthPing))]
public sealed partial class MessagingJsonContext : JsonSerializerContext;
