namespace OyatsuBiyori.Biz.Abstractions;

/// <summary>
/// すべてのエンティティの基底クラス。主キーはアプリ側で振る UUID（文字列）。
/// </summary>
public abstract class EntityBase
{
    /// <summary>主キー（UUID の文字列）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>更新日時（UTC）</summary>
    public DateTime UpdateDateTime { get; set; }

    /// <summary>更新した職員の ID。マスタのシードとシステムの処理では NULL</summary>
    public string? UpdateUserId { get; set; }
}
