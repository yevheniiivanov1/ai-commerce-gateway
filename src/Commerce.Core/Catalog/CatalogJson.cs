using System.Text.Json;
using System.Text.Json.Serialization;

namespace Commerce.Core.Catalog;

public static class CatalogJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new WallClockTimeConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Schedules are written as "07:00"; the built-in converter insists on seconds.</summary>
    private sealed class WallClockTimeConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeOnly.ParseExact(reader.GetString()!, ["HH:mm", "HH:mm:ss"], System.Globalization.CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }
}
