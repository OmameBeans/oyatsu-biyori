using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OyatsuBiyori.Data.DbContexts;

/// <summary>
/// DateTime を UTC として読み書きする。SQLite は Kind を持たないため、読み込んだ値に Kind=Utc を付ける（NFR-OPS-07）。
/// </summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

/// <summary>
/// 値を JSON の文字列として1列に保存する（配列や小さな構造）。
/// </summary>
internal static class JsonColumn
{
    // enum は文字列で保存する（enum の並びを変えてもデータの意味が変わらないように）
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>JSON の文字列にする</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>JSON の文字列から戻す</summary>
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

    /// <summary>JSON の変換</summary>
    public static ValueConverter<T, string> Converter<T>() => new(
        value => Serialize(value),
        json => Deserialize<T>(json));

    /// <summary>JSON の文字列で比べる（中身の変更を検知するため）</summary>
    public static ValueComparer<T> Comparer<T>() => new(
        (left, right) => Serialize(left) == Serialize(right),
        value => Serialize(value).GetHashCode(),
        value => Deserialize<T>(Serialize(value)));
}
