using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 日ごとの商品。価格・注文番号は献立に入れた時点の値を持つ。
/// </summary>
public sealed class MenuItem : EntityBase
{
    /// <summary>日ごとの献立</summary>
    public string MenuDayId { get; set; } = string.Empty;

    /// <summary>日ごとの献立</summary>
    [ForeignKey(nameof(MenuDayId))]
    public MenuDay MenuDay { get; set; } = null!;

    /// <summary>商品</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>商品</summary>
    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    /// <summary>1パックの税込価格（献立に入れた時点の掲載品の写しの価格）</summary>
    public int UnitPrice { get; set; }

    /// <summary>注文番号（献立に入れた時点の値）</summary>
    public string? OrderNo { get; set; }

    /// <summary>1人あたりの個数（既定は商品の既定値）</summary>
    public int PerChildCount { get; set; } = 1;

    /// <summary>パック数（保存する値。既定は式で求め、上書きできる。D-43）</summary>
    public int PackCount { get; set; }

    /// <summary>金額</summary>
    public int Amount { get; set; }

    /// <summary>確定時に、この商品の警告を職員が確認した日時（UTC。NULL＝未確認）</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>確認したときの判定の種類。今の判定と違えば確認していない扱いに戻す（SR-22）</summary>
    public AllergenJudgementKind? AcknowledgedJudgement { get; set; }

    /// <summary>
    /// 確認したときの判定の中身（未確認の品目・状態を決めている記録・避ける品目をまとめた値）。
    /// 種類が同じでも中身が変われば（例：避ける品目が増えて未確認の品目が増えた）、確認し直しにする（SR-16・SR-22）
    /// </summary>
    public string? AcknowledgedJudgementFingerprint { get; set; }
}
