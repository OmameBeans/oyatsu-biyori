using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// アレルゲン確認の記録の受け付け（SR-03・07・08・12・15、FR-PROD-06・06b、T1-19）。
/// 記録は追加のみ。ステータスは情報源で決める：パッケージなら「確認済み」、それ以外は「参考」。
/// Claude の読み取りをそのまま「確認済み」にしない（品目はすべて職員が選んだ値として受け取る）。
/// </summary>
public static class AllergenCheckFactory
{
    /// <summary>
    /// 確認の記録を作る。満たさなければ作らずにエラーを返す。
    /// - 確認済み（パッケージ）：職員が確かめた原材料の全文が必須（SR-12）。29品目と、避ける品目のうち施設の追加品目は、
    ///   すべて「含む / 含まない」を決めていること（SR-15。Claude が「含む」としなかった品目を自動で「含まない」にしない）
    /// - 参考（パッケージ以外）：全文はなくてよい。「含む」の品目だけを保存する（「含まない」の行は判定に使わないため捨てる）
    /// </summary>
    /// <param name="request">職員が確定した内容</param>
    /// <param name="productId">商品</param>
    /// <param name="allAllergens">マスタのすべての品目</param>
    /// <param name="avoidedAllergenIds">施設の避ける品目</param>
    /// <param name="checkedAtUtc">確認日時（UTC）</param>
    /// <param name="checkedBy">確認者（ログイン中のアカウント。FR-AUTH-04）</param>
    public static Result<AllergenCheck> Create(
        AllergenCheckRequest request,
        string productId,
        IEnumerable<Allergen> allAllergens,
        IEnumerable<string> avoidedAllergenIds,
        DateTime checkedAtUtc,
        string checkedBy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(allAllergens);
        ArgumentNullException.ThrowIfNull(avoidedAllergenIds);
        ArgumentException.ThrowIfNullOrEmpty(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkedBy);
        if (checkedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("確認日時は UTC で渡してください", nameof(checkedAtUtc));
        }

        if (!Enum.IsDefined(request.Source))
        {
            return Result<AllergenCheck>.Error("情報源を選んでください");
        }

        var allergensById = allAllergens.DistinctBy(a => a.Id, StringComparer.Ordinal).ToDictionary(a => a.Id, StringComparer.Ordinal);
        var result = new Result<AllergenCheck>();

        foreach (var unknown in request.Items.Where(i => !allergensById.ContainsKey(i.AllergenId)).Select(i => i.AllergenId).Distinct())
        {
            result.ErrorMessages.Add($"マスタにない品目が含まれています（{unknown}）。画面を読み込み直してください");
        }

        foreach (var group in request.Items.Where(i => allergensById.ContainsKey(i.AllergenId)).GroupBy(i => i.AllergenId).Where(g => g.Count() > 1))
        {
            result.ErrorMessages.Add($"「{allergensById[group.Key].Name}」が2回選ばれています。選び直してください");
        }

        var status = request.Source == AllergenCheckSource.Package ? AllergenCheckStatus.Confirmed : AllergenCheckStatus.Reference;
        if (status == AllergenCheckStatus.Confirmed)
        {
            if (string.IsNullOrWhiteSpace(request.RawIngredients))
            {
                result.ErrorMessages.Add("原材料の全文が空です。パッケージの原材料表示を読み取るか、手で入れてください（全文がないと「確認済み」にできません）");
            }

            var decided = request.Items.Select(i => i.AllergenId).ToHashSet(StringComparer.Ordinal);
            var avoided = avoidedAllergenIds.ToHashSet(StringComparer.Ordinal);
            var undecided = allergensById.Values
                .Where(a => a.Category != AllergenCategory.FacilityAdded || avoided.Contains(a.Id))
                .Where(a => !decided.Contains(a.Id))
                .OrderBy(a => a.SortOrder)
                .Select(a => a.Name)
                .ToList();
            if (undecided.Count > 0)
            {
                result.ErrorMessages.Add($"「含む / 含まない」を決めていない品目があります（{string.Join("・", undecided)}）。すべて決めてから記録してください");
            }
        }

        if (result.HasError)
        {
            return result;
        }

        var check = new AllergenCheck
        {
            ProductId = productId,
            Status = status,
            Source = request.Source,
            SourceNote = Blank(request.SourceNote),
            RawIngredientsOcr = Blank(request.RawIngredientsOcr),
            RawIngredients = Blank(request.RawIngredients),
            ContaminationNote = Blank(request.ContaminationNote),
            CheckedAt = checkedAtUtc,
            CheckedBy = checkedBy,
            UpdateDateTime = checkedAtUtc,
            UpdateUserId = checkedBy,
        };

        var items = status == AllergenCheckStatus.Confirmed ? request.Items : request.Items.Where(i => i.Contains);
        check.Items = items
            .OrderBy(i => allergensById[i.AllergenId].SortOrder)
            .Select(i => new AllergenCheckItem
            {
                AllergenCheckId = check.Id,
                AllergenId = i.AllergenId,
                Contains = i.Contains,
                UpdateDateTime = checkedAtUtc,
                UpdateUserId = checkedBy,
            })
            .ToList();

        result.Value = check;
        return result;
    }

    /// <summary>
    /// 職員が選んだタグを作る。味・種類は決まった語だけ（D-61）、キャラクターは空でないこと。同じタグは1つにする。
    /// </summary>
    public static Result<IReadOnlyList<ProductTag>> CreateTags(string productId, IEnumerable<ProductTagInput> tags, ProductTagSource source, DateTime nowUtc, string? userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(productId);
        ArgumentNullException.ThrowIfNull(tags);

        var result = new Result<IReadOnlyList<ProductTag>>();
        var created = new List<ProductTag>();
        foreach (var tag in tags)
        {
            var value = tag.Value?.Trim() ?? string.Empty;
            if (tag.Kind == ProductTagKind.Flavor && !ProductTagVocabulary.IsFlavor(value))
            {
                result.ErrorMessages.Add($"味・種類のタグ「{value}」は選べる語にありません。一覧から選んでください");
                continue;
            }

            if (!Enum.IsDefined(tag.Kind) || value.Length == 0)
            {
                result.ErrorMessages.Add("タグが空です。入れ直してください");
                continue;
            }

            if (created.Any(t => t.Kind == tag.Kind && t.Value == value))
            {
                continue;
            }

            created.Add(new ProductTag
            {
                ProductId = productId,
                Kind = tag.Kind,
                Value = value,
                Source = source,
                UpdateDateTime = nowUtc,
                UpdateUserId = userId,
            });
        }

        if (!result.HasError)
        {
            result.Value = created;
        }

        return result;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

/// <summary>確認の記録の入力（職員が確定した内容）</summary>
/// <param name="Source">情報源（パッケージなら「確認済み」、それ以外は「参考」）</param>
/// <param name="SourceNote">情報源の補足</param>
/// <param name="RawIngredientsOcr">Claude が読み取った原材料の全文（手入力なら null）</param>
/// <param name="RawIngredients">職員が確かめた原材料の全文</param>
/// <param name="ContaminationNote">同一工場・コンタミの注意書き</param>
/// <param name="Items">品目ごとの「含む / 含まない」（決めていない品目は入れない）</param>
public sealed record AllergenCheckRequest(
    AllergenCheckSource Source,
    string? SourceNote,
    string? RawIngredientsOcr,
    string? RawIngredients,
    string? ContaminationNote,
    IReadOnlyCollection<AllergenItemDecision> Items);

/// <summary>品目ごとの「含む / 含まない」</summary>
public sealed record AllergenItemDecision(string AllergenId, bool Contains);

/// <summary>タグの入力</summary>
public sealed record ProductTagInput(ProductTagKind Kind, string? Value);
