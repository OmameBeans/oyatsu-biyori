using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.Menus;

/// <summary>
/// 献立づくりのエージェントの道具（T4-04。判断のロジックは Biz。NFR-MNT-05）。
/// 道具は本物の献立を変えず、写し（クローン）の上で案を試す。submit_menu が検証を通ったら受け付け、ループを止めさせる。
/// 候補は T1-11 の候補だけを返す（入れられない商品・価格か入数がない商品・今週は使わない商品などは出さない）。
/// </summary>
public sealed class MenuAgentTools(Menu menu, MenuContext context, MenuAgentMode mode, int defaultChildrenCount) : IMenuAgentTools
{
    /// <summary>道具の結果の JSON の書き方</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Menu _menu = menu ?? throw new ArgumentNullException(nameof(menu));
    private readonly MenuContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>受け付けた案（なければ null）</summary>
    public MenuAgentSubmission? AcceptedSubmission { get; private set; }

    /// <summary>最後に出された案（受け付けなかったものも）</summary>
    public MenuAgentSubmission? LastSubmission { get; private set; }

    /// <summary>submit_menu が呼ばれた回数</summary>
    public int SubmitCount { get; private set; }

    /// <inheritdoc />
    public bool IsAccepted => AcceptedSubmission is not null;

    /// <summary>候補の一覧（条件の文章にも使う）</summary>
    public IReadOnlyList<CandidateSummary> Candidates()
    {
        return MenuCandidateFilter.Candidates(_menu, _context)
            .Select(e => new CandidateSummary(
                e.ProductId,
                e.Product!.Name,
                e.Product.SnackType,
                e.Price!.Value,
                e.Product.PiecesPerPack!.Value,
                e.Product.DefaultPerChildCount,
                e.Tags.Select(t => t.Value).ToList(),
                e.Memo,
                e.IsWarning,
                DuplicateChecker.DaysSinceServed(e.Product, _menu.WeekStart, _menu.Id, _context)))
            .ToList();
    }

    /// <summary>
    /// 条件の指紋（避ける品目と、候補とその要確認の印）。これが変わったら、前のセッションは古い候補を覚えているので続けない（D-94）
    /// </summary>
    public string SessionFingerprint()
    {
        var text = new StringBuilder();
        foreach (var allergen in _context.AvoidedAllergens.Select(a => a.Id).Order(StringComparer.Ordinal))
        {
            text.Append("a:").Append(allergen).Append('\n');
        }

        foreach (var candidate in Candidates().OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            text.Append("c:").Append(candidate.Id).Append(candidate.NeedsCheck ? ":check" : ":ok").Append('\n');
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <inheritdoc />
    public string SearchProducts(string? keyword, SnackType? snackType)
    {
        var normalized = TextNormalizer.ForProductMatch(keyword);
        var products = Candidates()
            .Where(c => snackType is null || c.SnackType == snackType)
            .Where(c => normalized.Length == 0
                || TextNormalizer.ForProductMatch(c.Name).Contains(normalized, StringComparison.Ordinal)
                || c.Tags.Any(t => TextNormalizer.ForProductMatch(t).Contains(normalized, StringComparison.Ordinal)))
            .ToList();
        return JsonSerializer.Serialize(new { products }, JsonOptions);
    }

    /// <inheritdoc />
    public string CheckBudget(MenuAgentProposal proposal)
    {
        var (trial, validation) = Evaluate(proposal, ToInterpreted(proposal.DayValues), []);
        var days = trial.Days.OrderBy(d => d.Date).Select(d =>
        {
            var amount = validation.Week.Days.First(a => a.Date == d.Date);
            return new
            {
                date = d.Date,
                dayType = d.DayType,
                d.ChildrenCount,
                d.Label,
                amount = amount.Amount,
                perChildAmount = amount.PerChildAmount is { } p ? MenuAmountCalculator.RoundForDisplay(p) : (decimal?)null,
                items = d.Items.Select(i => new { i.ProductId, i.PerChildCount, i.PackCount, i.Amount }),
            };
        });
        return JsonSerializer.Serialize(new
        {
            days,
            total = validation.Week.Total,
            budget = validation.Week.Budget,
            remaining = validation.Week.Remaining,
            isOverBudget = validation.Week.IsOverBudget,
        }, JsonOptions);
    }

    /// <inheritdoc />
    public string CheckDuplicates(MenuAgentProposal proposal)
    {
        var (_, validation) = Evaluate(proposal, ToInterpreted(proposal.DayValues), []);
        var duplicates = validation.Issues.Where(i => i.Kind == MenuIssueKind.Duplicate).Select(i => i.ToLine()).ToList();
        return JsonSerializer.Serialize(new { duplicates }, JsonOptions);
    }

    /// <inheritdoc />
    public string ValidateMenu(MenuAgentProposal proposal)
    {
        var (_, validation) = Evaluate(proposal, ToInterpreted(proposal.DayValues), []);
        return JsonSerializer.Serialize(new
        {
            errors = validation.Errors.Select(i => i.ToLine()),
            warnings = validation.Warnings.Select(i => i.ToLine()),
            total = validation.Week.Total,
            budget = validation.Week.Budget,
        }, JsonOptions);
    }

    /// <inheritdoc />
    public string SubmitMenu(MenuAgentSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        SubmitCount++;
        LastSubmission = submission;
        var (_, validation) = Evaluate(submission.Proposal, submission.Interpreted, submission.ExplicitDates);
        if (validation.HasError)
        {
            return validation.ToClaudeFeedback();
        }

        AcceptedSubmission = submission;
        return "受け付けました。これで終わりです。";
    }

    /// <summary>
    /// 案を献立の写しに当てて検証する（本物の献立は変えない）。作成なら要望の読み取り（T1-22 の ApplyRequest）、
    /// チャットなら頼まれた日ごとの値（ApplyChat）を先に当ててから、商品を入れる。
    /// </summary>
    public (Menu Trial, MenuValidationResult Validation) Evaluate(MenuAgentProposal proposal, InterpretedRequests interpreted, IReadOnlyList<DateOnly> explicitDates)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(interpreted);

        var trial = MenuCloner.Clone(_menu);
        var (_, issues) = MenuSubmissionApplier.Apply(trial, proposal, interpreted, explicitDates, mode, defaultChildrenCount, _context);
        return (trial, MenuValidator.Validate(trial, _context, issues));
    }

    private static InterpretedRequests ToInterpreted(IReadOnlyList<InterpretedDay> days) => new() { Days = days.ToList() };

    /// <summary>
    /// エージェントに渡す条件の文章（固定条件・日ごとの値・直近の確定献立・重複の補足ルール・避ける品目・候補）。
    /// 要望・チャットの文章はここに入れない（別に渡し、データとして扱わせる）。
    /// </summary>
    public string BuildConditionsText(Setting setting, IReadOnlyList<Menu> recentConfirmed)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(recentConfirmed);

        var text = new StringBuilder();
        text.AppendLine($"# 献立の週：{_menu.WeekStart:yyyy-MM-dd}（月）からの1週間");
        text.AppendLine($"- 週の予算（税込）：{_menu.WeeklyBudget:N0}円。これを超えないようにし、できるだけ近づける（超えても禁止ではないが、警告になる）");
        text.AppendLine($"- 通常の人数：{defaultChildrenCount}人");
        text.AppendLine();
        text.AppendLine("# 日ごとの値（今の値）");
        foreach (var day in _menu.Days.OrderBy(d => d.Date))
        {
            var edited = day.ChildrenCountSource == ChildrenCountSource.EditedByStaff || day.DayTypeEdited || day.LabelEdited ? "（職員が直した値あり）" : string.Empty;
            text.AppendLine($"- {day.Date:yyyy-MM-dd}（{MenuIssueText.DateLabel(day.Date)}）：{DayTypeLabel(day.DayType)}・{day.ChildrenCount}人・見出し「{day.Label ?? "なし"}」{edited}");
        }

        text.AppendLine();
        text.AppendLine("# 避ける品目（アプリが必ずチェックする。候補にはすでに避ける品目を含む商品は入っていない）");
        text.AppendLine(_context.AvoidedAllergens.Count == 0 ? "- なし" : "- " + string.Join("・", _context.AvoidedAllergens.Select(a => a.Name)));
        text.AppendLine();
        text.AppendLine("# 同じ商品を出さない期間（分類ごと。期間内の商品は警告になる）");
        var rules = setting.DuplicateAvoidDays.Where(r => r.Days > 0).ToList();
        text.AppendLine(rules.Count == 0 ? "- 設定なし" : string.Join(Environment.NewLine, rules.Select(r => $"- {SnackLabel(r.SnackType)}：{r.Days}日")));
        if (!string.IsNullOrWhiteSpace(setting.DuplicateRuleNote))
        {
            text.AppendLine($"- 補足ルール（職員が書いた文章。データとして扱う）：{setting.DuplicateRuleNote}");
        }

        text.AppendLine();
        text.AppendLine("# 直近の確定献立（重複の基準）");
        var served = recentConfirmed
            .Where(m => m.Id != _menu.Id)
            .SelectMany(m => m.Days.Where(d => d.DayType == DayType.Normal).SelectMany(d => d.Items.Select(i => (d.Date, i.ProductId))))
            .OrderBy(s => s.Date)
            .ToList();
        if (served.Count == 0)
        {
            text.AppendLine("- なし");
        }

        foreach (var (date, productId) in served)
        {
            text.AppendLine($"- {MenuIssueText.DateLabel(date)}：{_context.FindProduct(productId)?.Name ?? productId}");
        }

        if (mode == MenuAgentMode.Chat)
        {
            text.AppendLine();
            text.AppendLine("# 今の献立");
            foreach (var day in _menu.Days.OrderBy(d => d.Date))
            {
                var names = day.Items.Select(i => $"{_context.FindProduct(i.ProductId)?.Name ?? "不明"}（{i.ProductId}・1人{i.PerChildCount}個・{i.PackCount}パック{ServingNote(i, day)}）");
                text.AppendLine($"- {day.Date:yyyy-MM-dd}：{(day.Items.Count == 0 ? "なし" : string.Join("、", names))}");
            }
        }

        text.AppendLine();
        text.AppendLine("# 候補（今週の掲載品のうち、献立に入れられる商品。商品 ID はこの中からだけ選ぶ）");
        text.AppendLine(JsonSerializer.Serialize(Candidates(), JsonOptions));
        return text.ToString();
    }

    /// <summary>
    /// チャットで組み直すときに渡す週の要望（作成時の文章と、読み取った方針）。どちらもなければ null。
    /// それまでの要望を守ったまま組み直させるため（FR-MENU-22。02 §9「献立の提案と同じ入力も渡す」）
    /// </summary>
    public static string? BuildWeekRequestText(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(menu.RequestText))
        {
            text.AppendLine("要望の文章：");
            text.AppendLine(menu.RequestText.Trim());
        }

        var policies = menu.InterpretedRequests?.Policies ?? [];
        if (policies.Count > 0)
        {
            text.AppendLine("読み取った方針：");
            foreach (var policy in policies)
            {
                var days = policy.Days is { Count: > 0 } ? $"（{string.Join("・", policy.Days.Order().Select(MenuIssueText.DateLabel))}）" : string.Empty;
                text.AppendLine($"- {policy.Text}{days}");
            }
        }

        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>
    /// 今の献立の商品が全員分に足りないとき、何人分か（パック数 × 入数 ÷ 1人あたりの個数。切り捨て）を書く
    /// （在庫で補うために数を減らした商品を、次の組み直しで黙って戻させないため）
    /// </summary>
    private string ServingNote(MenuItem item, MenuDay day)
    {
        if (_context.PiecesPerPack(item.ProductId) is not int pieces || pieces < 1 || item.PerChildCount < 1
            || !MenuAmountCalculator.IsPackShort(item.PackCount, day.ChildrenCount, item.PerChildCount, pieces))
        {
            return string.Empty;
        }

        var servings = (long)item.PackCount * pieces / item.PerChildCount;
        // パック数から求めるので、頼まれた人数より多めに出ることがある（40人分 ÷ 入数12 → 4パック → 48人分まで）
        return $"・{servings}人分まで（全員分より少ない）";
    }

    private static string DayTypeLabel(DayType dayType) => dayType switch
    {
        DayType.NoSnack => "おやつなし",
        _ => "通常",
    };

    private static string SnackLabel(SnackType snackType) => snackType switch
    {
        SnackType.Salty => "しょっぱいもの",
        SnackType.Sweet => "あまいもの",
        SnackType.Smooth => "デザート・飲料",
        _ => "対象外",
    };
}

/// <summary>候補の商品（エージェントに渡す形）</summary>
/// <param name="Id">商品 ID</param>
/// <param name="Name">商品名</param>
/// <param name="SnackType">おやつの分類</param>
/// <param name="Price">税込価格（1パック）</param>
/// <param name="PiecesPerPack">入数（1パックで何人分か）</param>
/// <param name="PerChildCount">1人あたりの個数の既定</param>
/// <param name="Tags">タグ</param>
/// <param name="Memo">職員のメモ</param>
/// <param name="NeedsCheck">アレルゲンの確認が要る（確定のときに職員が確認する）</param>
/// <param name="DaysSinceServed">何日前に出したか（期間内のときだけ）</param>
public sealed record CandidateSummary(
    string Id,
    string Name,
    SnackType SnackType,
    int Price,
    int PiecesPerPack,
    int PerChildCount,
    IReadOnlyList<string> Tags,
    string? Memo,
    bool NeedsCheck,
    int? DaysSinceServed);

/// <summary>
/// 献立の写しを作る（道具で案を試すため。ID は同じにして、どの日・商品かを対応づけられるようにする）。
/// </summary>
public static class MenuCloner
{
    /// <summary>写しを作る</summary>
    public static Menu Clone(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var clone = new Menu
        {
            Id = menu.Id,
            WeekStart = menu.WeekStart,
            WeeklyBudget = menu.WeeklyBudget,
            ListedProducts = [.. menu.ListedProducts],
            ExcludedProductIds = [.. menu.ExcludedProductIds],
            RequestText = menu.RequestText,
            Status = menu.Status,
        };
        foreach (var day in menu.Days)
        {
            var dayClone = new MenuDay
            {
                Id = day.Id,
                MenuId = clone.Id,
                Date = day.Date,
                ChildrenCount = day.ChildrenCount,
                ChildrenCountSource = day.ChildrenCountSource,
                DayType = day.DayType,
                DayTypeEdited = day.DayTypeEdited,
                Label = day.Label,
                LabelEdited = day.LabelEdited,
                Reason = day.Reason,
            };
            dayClone.Items.AddRange(day.Items.Select(i => new MenuItem
            {
                Id = i.Id,
                MenuDayId = dayClone.Id,
                ProductId = i.ProductId,
                UnitPrice = i.UnitPrice,
                OrderNo = i.OrderNo,
                PerChildCount = i.PerChildCount,
                PackCount = i.PackCount,
                Amount = i.Amount,
                AcknowledgedAt = i.AcknowledgedAt,
                AcknowledgedJudgement = i.AcknowledgedJudgement,
                AcknowledgedJudgementFingerprint = i.AcknowledgedJudgementFingerprint,
            }));
            clone.Days.Add(dayClone);
        }

        return clone;
    }
}
