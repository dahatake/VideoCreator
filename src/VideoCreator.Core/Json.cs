using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoCreator;

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create(indented: false);
    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return o;
    }

    public static string Serialize<T>(T value, bool indented = false) => JsonSerializer.Serialize(value, indented ? Indented : Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"null {typeof(T).Name}");
}
