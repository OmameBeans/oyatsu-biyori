using System.Security.Cryptography;
using System.Text;

namespace OyatsuBiyori.Biz.MasterData;

/// <summary>
/// 初期データの主キー。名前から毎回同じ UUID を作る（マイグレーションのたびに差分が出ないように）。
/// </summary>
public static class SeedIds
{
    /// <summary>初期データの更新日時（固定）</summary>
    public static readonly DateTime SeededAt = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>名前から UUID を作る</summary>
    public static string For(string name)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes("oyatsu-biyori:" + name));
        var bytes = hash[..16];

        // UUID の版（5）と変種を設定する
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true).ToString();
    }
}
