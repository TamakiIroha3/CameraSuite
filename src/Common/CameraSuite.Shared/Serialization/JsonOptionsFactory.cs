using System.Text.Json;
using System.Text.Json.Serialization;

namespace CameraSuite.Shared.Serialization;

public static class JsonOptionsFactory
{
    public static JsonSerializerOptions CreateDefault()
        => new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            },
        };
}
