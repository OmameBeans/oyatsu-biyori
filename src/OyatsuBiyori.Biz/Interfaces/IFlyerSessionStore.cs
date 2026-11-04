using OyatsuBiyori.Biz.FlyerImport;

namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// 確定前のチラシの読み取り結果を、献立と結びつけてメモリに持つ（24時間。再起動したら消える。FR-FLYER-10）。
/// </summary>
public interface IFlyerSessionStore
{
    /// <summary>献立の取り込み（なければ・期限切れなら null）</summary>
    FlyerImportSession? Get(string menuId);

    /// <summary>献立の取り込みを置く（前のものは捨てる）</summary>
    void Set(FlyerImportSession session);

    /// <summary>献立の取り込みを捨てる</summary>
    void Remove(string menuId);
}
