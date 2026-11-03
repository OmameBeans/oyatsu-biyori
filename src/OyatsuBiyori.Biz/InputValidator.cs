using System.Text;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Extensions;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 入力の検証（FR-COND 受け入れ条件、FR-AUTH-03a、02 §5「金額計算の定義」、T1-21）。
/// 業務エラーは例外にせず、次にすることを含む日本語のメッセージで返す。
/// </summary>
public static class InputValidator
{
    /// <summary>パスワードの最低の文字数（FR-AUTH-03a）</summary>
    public const int MinPasswordLength = 12;

    /// <summary>条件設定（SC-03）の検証</summary>
    public static Result ValidateSettings(SettingsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var result = new Result();
        AddIfError(result, PositiveInteger(input.DefaultChildrenCount, "通常の人数"));
        AddIfError(result, PositiveInteger(input.DefaultWeeklyBudget, "既定の週の予算"));
        AddIfError(result, PositiveInteger(input.RecheckDays, "再確認期間（日）"));

        if (input.ServingWeekdays.Count == 0)
        {
            result.ErrorMessages.Add("おやつを出す曜日を1つ以上選んでください");
        }
        else if (input.ServingWeekdays.Any(d => !Enum.IsDefined(d)))
        {
            result.ErrorMessages.Add("おやつを出す曜日が正しくありません。選び直してください");
        }
        else if (input.ServingWeekdays.Distinct().Count() != input.ServingWeekdays.Count)
        {
            result.ErrorMessages.Add("おやつを出す曜日が重なっています。選び直してください");
        }

        foreach (var message in DuplicateRuleErrors(input.DuplicateAvoidDays))
        {
            result.ErrorMessages.Add(message);
        }

        return result;
    }

    /// <summary>
    /// 重複させない期間の検証：対象外は不可、同じ分類は1件、日数は1以上（「設定なし」は配列に入れない）。
    /// </summary>
    public static IReadOnlyList<string> DuplicateRuleErrors(IReadOnlyCollection<DuplicateAvoidRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var errors = new List<string>();
        foreach (var rule in rules)
        {
            if (!Enum.IsDefined(rule.SnackType))
            {
                errors.Add("重複させない期間の分類が正しくありません");
            }
            else if (rule.SnackType == SnackType.NotSnack)
            {
                errors.Add("「対象外」の分類には重複させない期間を設定できません");
            }

            if (rule.Days < 1)
            {
                errors.Add($"「{SnackLabel(rule.SnackType)}」の重複させない期間は1以上の日数にしてください（期間を設けないときは「設定なし」を選んでください）");
            }
        }

        foreach (var group in rules.GroupBy(r => r.SnackType).Where(g => g.Count() > 1))
        {
            errors.Add($"「{SnackLabel(group.Key)}」の重複させない期間が2つあります。1つにしてください");
        }

        return errors;
    }

    /// <summary>人数の上限（学童保育の規模を大きく超える値は、読み違い・打ち間違いとして受け付けない）</summary>
    public const int MaxChildrenCount = 1000;

    /// <summary>日ごとの人数（0以上の整数。0はおやつを出さない日）</summary>
    public static Result ValidateDayChildrenCount(int childrenCount)
    {
        if (childrenCount < 0)
        {
            return Result.Error("その日の人数は0以上の整数で入れてください");
        }

        return childrenCount > MaxChildrenCount
            ? Result.Error($"その日の人数が多すぎます（{MaxChildrenCount}人まで）。入れ直してください")
            : new Result();
    }

    /// <summary>入数（1以上の整数）</summary>
    public static Result ValidatePiecesPerPack(int? piecesPerPack) => PositiveInteger(piecesPerPack, "入数");

    /// <summary>1人あたりの個数（1以上の整数）</summary>
    public static Result ValidatePerChildCount(int? perChildCount) => PositiveInteger(perChildCount, "1人あたりの個数");

    /// <summary>税込価格（1以上の整数）</summary>
    public static Result ValidatePrice(int? price) => PositiveInteger(price, "税込価格");

    /// <summary>パック数（1以上の整数。0にしたいときは商品を外す）</summary>
    public static Result ValidatePackCount(int? packCount) => PositiveInteger(packCount, "パック数");

    /// <summary>週の予算（1以上の整数）</summary>
    public static Result ValidateWeeklyBudget(int? budget) => PositiveInteger(budget, "週の予算");

    /// <summary>探す言葉（探す言葉の正規化をしたあとに空にならないこと）</summary>
    public static Result ValidateKeyword(string? keyword)
    {
        return TextNormalizer.ForKeywordSearch(keyword).Length == 0
            ? Result.Error("探す言葉が空か、記号だけです。原材料名に出てくる言葉を入れてください")
            : new Result();
    }

    /// <summary>
    /// パスワード：12文字以上（文字の種類は問わない）、ユーザー名と同じものは不可（FR-AUTH-03a）。
    /// 文字数は見た目の1文字（サロゲートペアを1文字）で数える。
    /// </summary>
    public static Result ValidatePassword(string? password, string? userName)
    {
        var result = new Result();
        if (string.IsNullOrEmpty(password) || password.EnumerateRunes().Count() < MinPasswordLength)
        {
            result.ErrorMessages.Add($"パスワードは{MinPasswordLength}文字以上にしてください");
        }

        if (!string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(userName)
            && string.Equals(password.Trim().Normalize(NormalizationForm.FormKC), userName.Trim().Normalize(NormalizationForm.FormKC), StringComparison.OrdinalIgnoreCase))
        {
            result.ErrorMessages.Add("ユーザー名と同じパスワードは使えません");
        }

        return result;
    }

    private static string SnackLabel(SnackType snackType) => Enum.IsDefined(snackType) ? snackType.ToDisplayString() : "不明な分類";

    private static Result PositiveInteger(int? value, string label)
    {
        return value is null or < 1 ? Result.Error($"{label}は1以上の整数で入れてください") : new Result();
    }

    private static void AddIfError(Result result, Result check)
    {
        result.ErrorMessages.AddRange(check.ErrorMessages);
    }
}

/// <summary>条件設定の入力</summary>
/// <param name="DefaultChildrenCount">通常の人数</param>
/// <param name="DefaultWeeklyBudget">既定の週の予算</param>
/// <param name="ServingWeekdays">おやつを出す曜日</param>
/// <param name="RecheckDays">再確認期間（日）</param>
/// <param name="DuplicateAvoidDays">分類ごとの重複させない期間（「設定なし」の分類は入れない）</param>
public sealed record SettingsInput(
    int? DefaultChildrenCount,
    int? DefaultWeeklyBudget,
    IReadOnlyCollection<DayOfWeek> ServingWeekdays,
    int? RecheckDays,
    IReadOnlyCollection<DuplicateAvoidRule> DuplicateAvoidDays);
