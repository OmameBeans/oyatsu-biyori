using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OyatsuBiyori.Claude.Services;

/// <summary>
/// 構造化出力の JSON スキーマを C# の型から作る。
/// 構造化出力の決まりに合わせ、オブジェクトには additionalProperties: false を付け、すべての項目を required にする。
/// 商品 ID などの動的な値はスキーマの enum にせず、Biz で検証する。
/// </summary>
internal static class JsonSchemas
{
    /// <summary>Claude の出力を読み書きするときの設定（キャメルケース・enum は文字列）</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),

        // 数値は数値だけを受け付ける（文字列の数値を許すと、スキーマが "string" との組み合わせになるため）
        NumberHandling = JsonNumberHandling.Strict,

        // 項目の抜け・null・知らない項目を黙って既定値で埋めず、エラーにする（「含む品目」が抜けた答えを「空」として通さないため）
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>型からスキーマを作る</summary>
    public static Dictionary<string, JsonElement> For<T>()
    {
        var node = SerializerOptions.GetJsonSchemaAsNode(typeof(T), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
        });
        Tighten(node);

        var result = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in node.AsObject())
        {
            result[key] = JsonSerializer.SerializeToElement(value);
        }

        return result;
    }

    /// <summary>オブジェクトに additionalProperties: false を付け、すべての項目を required にする（入れ子も含む）</summary>
    private static void Tighten(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject properties)
                {
                    obj["additionalProperties"] = false;
                    obj["required"] = new JsonArray(properties.Select(p => (JsonNode)p.Key).ToArray());
                }

                foreach (var (_, child) in obj.ToList())
                {
                    Tighten(child);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    Tighten(child);
                }

                break;
        }
    }
}
