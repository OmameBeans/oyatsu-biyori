using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// Claude API の呼び出しの記録（本文・画像は保存しない）。費用の計算と1日の上限に使う。
/// </summary>
public sealed class ApiUsageLog : EntityBase
{
    /// <summary>呼び出した日時（UTC）</summary>
    public DateTime CalledAt { get; set; }

    /// <summary>用途</summary>
    public ClaudePurpose Purpose { get; set; }

    /// <summary>モデル名</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>入力トークン数</summary>
    public long InputTokens { get; set; }

    /// <summary>出力トークン数</summary>
    public long OutputTokens { get; set; }

    /// <summary>キャッシュの書き込みの入力トークン数</summary>
    public long CacheCreationInputTokens { get; set; }

    /// <summary>キャッシュの読み込みの入力トークン数</summary>
    public long CacheReadInputTokens { get; set; }

    /// <summary>成功したか</summary>
    public bool Succeeded { get; set; }
}
