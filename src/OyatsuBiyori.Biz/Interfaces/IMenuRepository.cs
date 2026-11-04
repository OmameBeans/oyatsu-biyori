using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// 献立（取得したものは追跡され、IUnitOfWork で保存する）。献立は日ごとの献立と商品を読み込んで返す。
/// </summary>
public interface IMenuRepository
{
    /// <summary>献立（なければ null）</summary>
    Task<Menu?> GetAsync(string menuId);

    /// <summary>週の初日の献立（なければ null）</summary>
    Task<Menu?> GetByWeekStartAsync(DateOnly weekStart);

    /// <summary>すべての献立（一覧用）</summary>
    Task<IReadOnlyList<Menu>> GetAllAsync();

    /// <summary>確定献立のうち、期間（日付）に日を持つもの（重複の基準）</summary>
    Task<IReadOnlyList<Menu>> GetConfirmedInRangeAsync(DateOnly from, DateOnly to);

    /// <summary>指定した日以降の日を持つ献立（届いた後の影響を求める）</summary>
    Task<IReadOnlyList<Menu>> GetFromDateAsync(DateOnly from);

    /// <summary>チャットの履歴（古い順）</summary>
    Task<IReadOnlyList<MenuMessage>> GetMessagesAsync(string menuId);

    /// <summary>
    /// 画面が読み込んだときの row_version を設定する（保存時にこれと違えば ConcurrencyConflictException。D-46）。
    /// </summary>
    void SetExpectedRowVersion(Menu menu, byte[] rowVersion);

    /// <summary>保存したあとの row_version（トリガーで変わる）を読み直す</summary>
    Task RefreshRowVersionAsync(Menu menu);

    /// <summary>献立を足す</summary>
    void Add(Menu menu);

    /// <summary>献立を消す（日ごとの献立・商品・チャットも消える）</summary>
    void Remove(Menu menu);

    /// <summary>日ごとの商品を消す</summary>
    void RemoveItem(MenuItem item);

    /// <summary>チャットの発言を足す</summary>
    void AddMessage(MenuMessage message);

    /// <summary>チャットの履歴を消す</summary>
    void RemoveMessages(IEnumerable<MenuMessage> messages);
}
