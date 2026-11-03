using System.Security.Cryptography;
using System.Text;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 判定の中身の指紋（menu_items.acknowledged_judgement_fingerprint。SR-16・SR-22、T1-16）。
/// 確定時に職員が確認した判定と今の判定を、種類だけでなく中身でも比べる。種類が同じでも、未確認の品目・状態を決めている記録・
/// 避ける品目が変われば指紋が変わり、確認し直しになる。記録は追加のみなので、記録の Id がそのまま版として働く（SR-07）。
/// </summary>
public static class AllergenJudgementFingerprint
{
    /// <summary>指紋の作り方の版。中身の構成を変えたら上げる（すべての確認が未確認に戻る）</summary>
    public const string Version = "v1";

    /// <summary>指紋を求める</summary>
    /// <param name="productId">商品（記録のない商品どうしでも、別の商品に確認が引き継がれないように入れる）</param>
    /// <param name="judgement">今の判定</param>
    /// <param name="avoidedAllergenIds">判定に使った避ける品目</param>
    public static string Compute(string productId, AllergenJudgement judgement, IEnumerable<string> avoidedAllergenIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(productId);
        ArgumentNullException.ThrowIfNull(judgement);
        ArgumentNullException.ThrowIfNull(avoidedAllergenIds);

        var parts = new[]
        {
            "product=" + productId,
            "kind=" + judgement.Kind,
            "category=" + judgement.Category,
            "latest=" + judgement.LatestCheckId,
            "latestConfirmed=" + judgement.LatestConfirmedCheckId,
            "avoided=" + Join(avoidedAllergenIds),
            "unconfirmed=" + Join(judgement.UnconfirmedAllergens.Select(a => a.Id)),
            "contained=" + Join(judgement.ContainedAllergens.Select(a => a.Id)),
            "keywordHits=" + Join(judgement.KeywordHits.Select(h => h.Match.AllergenId)),
        };

        var canonical = string.Join("\n", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Version + ":" + Convert.ToHexStringLower(hash);
    }

    private static string Join(IEnumerable<string> ids) => string.Join(",", ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
}
