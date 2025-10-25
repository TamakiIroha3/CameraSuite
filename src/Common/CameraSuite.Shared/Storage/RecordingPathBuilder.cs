using System.Text;
using CameraSuite.Shared.Configuration;

namespace CameraSuite.Shared.Storage;

public static class RecordingPathBuilder
{
    public static (string Directory, string FilePath) Build(RecordingOptions options, string channelName, DateTimeOffset timestampUtc)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var safeChannel = Sanitize(channelName);
        var timestamp = timestampUtc.ToUniversalTime();

        var directory = Path.Combine(
            options.RootDirectory,
            safeChannel,
            timestamp.ToString("yyyyMMdd"));

        var fileName = $"{safeChannel}_{timestamp:yyyyMMdd_HHmmss}.ts";

        return (directory, Path.Combine(directory, fileName));
    }

    private static string Sanitize(string channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            return "unknown";
        }

        Span<char> invalidChars = stackalloc char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', };
        var builder = new StringBuilder(channelName.Length);
        foreach (var ch in channelName)
        {
            if (invalidChars.Contains(ch) || char.IsControl(ch))
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
