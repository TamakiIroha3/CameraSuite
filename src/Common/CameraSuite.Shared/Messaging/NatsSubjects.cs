namespace CameraSuite.Shared.Messaging;

public static class NatsSubjects
{
    public const string AuthRequests = "auth.requests";

    public static string AuthResponses(string sourceId) => $"auth.responses.{sourceId}";

    public const string StreamsAnnounce = "streams.announce";

    public const string StreamsState = "streams.state";

    public static string SourceCommands(string sourceId) => $"sources.{sourceId}.commands";

    public static string SourceTelemetry(string sourceId) => $"sources.{sourceId}.telemetry";

    public static string ViewerTelemetry(string viewerId) => $"viewers.{viewerId}.telemetry";

    public static string Health(string component) => $"health.{component}";
}
