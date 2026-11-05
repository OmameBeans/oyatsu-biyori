using System.Text.Json;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Server.E2E;

/// <summary>
/// E2E 用の偽のチラシの読み取り。画像は見ずに、架空のチラシの正解データ（samples/flyers/*.expected.json）を返す。
/// 費用がかからず、毎回同じ結果になる。アプリの検証・アレルゲンの判定は本物を通す。
/// </summary>
public sealed class FakeFlyerReader(IHostEnvironment environment, IConfiguration configuration) : IFlyerReader
{
    private readonly IHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public async Task<Result<FlyerReading>> ReadAsync(IReadOnlyList<ClaudeImage> images, IReadOnlyList<Allergen> allergens, CancellationToken cancellationToken)
    {
        var relative = _configuration["E2E:FlyerSample"] ?? "../../samples/flyers/flyer-01.expected.json";
        var path = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, relative));
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var items = new List<FlyerReadProduct>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var labels = item.GetProperty("allergenLabel").EnumerateArray().Select(e => e.GetString()!).ToList();
            var labelText = labels.Count == 0 ? null : string.Join("・", labels);
            var parsed = FlyerAllergenPlanner.ParseLabel(labelText, allergens);
            var codes = parsed.Allergens.Select(a => allergens.First(x => x.Id == a.Id).Code).ToList();
            var pieces = item.GetProperty("piecesPerPackCandidates").EnumerateArray().Select(e => e.GetInt32()).ToList();
            items.Add(new FlyerReadProduct(
                item.GetProperty("name").GetString()!,
                item.GetProperty("maker").GetString(),
                item.GetProperty("volume").GetString(),
                item.GetProperty("priceExcludingTax").GetInt32(),
                item.GetProperty("priceIncludingTax").GetInt32(),
                item.GetProperty("code").GetString(),
                item.GetProperty("ageLabel").GetString(),
                labelText,
                labels.Count == 0,
                codes,
                parsed.UnmappedTerms,
                item.GetProperty("description").GetString(),
                item.TryGetProperty("bulk", out var bulk) && bulk.ValueKind == JsonValueKind.String ? bulk.GetString() : null,
                pieces.Count == 0 ? null : pieces[0],
                pieces.Count == 0 ? "内容量から入数が分かりません" : "内容量の表記から求めました",
                SnackTypeFrom(item.GetProperty("snackCategory").GetString()),
                [],
                []));
        }

        return Result<FlyerReading>.Ok(new FlyerReading(items));
    }

    private static SnackType? SnackTypeFrom(string? label) => label switch
    {
        "しょっぱいもの" => SnackType.Salty,
        "あまいもの" => SnackType.Sweet,
        "デザート・飲料" => SnackType.Smooth,
        "対象外" => SnackType.NotSnack,
        _ => null,
    };
}

/// <summary>
/// E2E 用の偽の原材料表示の読み取り。商品名で架空のパッケージの正解データ（samples/packages/*.expected.json）を選んで返す。
/// 「含む」の候補はアレルゲン表示の品目だけにする（原材料名にだけあるヘーゼルナッツは、探す言葉で見つかることを確かめるため。T5-07）。
/// 全文には、写真の改行のような改行を「ヘーゼルナッ」と「ツペースト」のあいだに入れる（正規化で見落とさないことを確かめるため）。
/// </summary>
public sealed class FakeAllergenExtractor(IHostEnvironment environment) : IAllergenExtractor
{
    private readonly IHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public async Task<Result<AllergenExtraction>> ExtractAsync(IReadOnlyList<ClaudeImage> images, IReadOnlyList<Allergen> allergens, string productName, CancellationToken cancellationToken)
    {
        var sample = productName.Contains("おかき", StringComparison.Ordinal) ? "package-8119" : "package-8104";
        var path = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "../../samples/packages", sample + ".expected.json"));
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var text = root.GetProperty("ingredientsText").GetString() ?? string.Empty;
        text = text.Replace("ヘーゼルナッツペースト", "ヘーゼルナッ\nツペースト", StringComparison.Ordinal);
        var codes = root.GetProperty("labelAllergens").EnumerateArray()
            .Select(e => e.GetString())
            .Select(name => allergens.FirstOrDefault(a => a.Name == name)?.Code)
            .Where(code => code is not null)
            .Select(code => code!)
            .ToList();
        var tags = root.GetProperty("tags").EnumerateArray().Select(e => e.GetString()!).ToList();
        return Result<AllergenExtraction>.Ok(new AllergenExtraction(
            text,
            codes,
            null,
            tags.Where(t => !ProductTagVocabulary.IsFlavor(t)).ToList(),
            tags.Where(ProductTagVocabulary.IsFlavor).ToList()));
    }
}

/// <summary>E2E 用の偽の読み取りの検証（決まった指摘を1つ返す）</summary>
public sealed class FakeFlyerReviewer : IFlyerReviewer
{
    public Task<Result<FlyerReview>> ReviewAsync(IReadOnlyList<ClaudeImage> images, FlyerReading reading, CancellationToken cancellationToken)
    {
        var notes = reading.Items.Count == 0
            ? []
            : new List<FlyerReviewNote> { new(0, "入数", "個包装の数は説明文にしか書かれていません。入数を確かめてください") };
        return Task.FromResult(Result<FlyerReview>.Ok(new FlyerReview(notes, [], [])));
    }
}

/// <summary>E2E 用の偽の照合の判定（よく似ていれば味違い、そうでなければ別の商品）</summary>
public sealed class FakeProductMatchJudge : IProductMatchJudge
{
    public Task<Result<ProductMatchJudgement>> JudgeAsync(ProductMatchInput item, IReadOnlyList<Product> candidates, CancellationToken cancellationToken)
    {
        var name = TextNormalizer.ForProductMatch(item.Name);
        var best = candidates
            .Select(c => (Product: c, Score: ProductMatcher.DiceCoefficient(name, TextNormalizer.ForProductMatch(c.Name))))
            .OrderByDescending(c => c.Score)
            .FirstOrDefault();
        var judgement = best.Product is not null && best.Score >= 0.5
            ? new ProductMatchJudgement(ProductMatchVerdict.FlavorVariant, best.Product.Id, "商品名の大部分が同じで、味の部分だけが違います")
            : new ProductMatchJudgement(ProductMatchVerdict.Different, null, "商品名が違うため、別の商品と考えられます");
        return Task.FromResult(Result<ProductMatchJudgement>.Ok(judgement));
    }
}

/// <summary>E2E 用の偽の予算の説明（候補を順に選ぶ）</summary>
public sealed class FakeBudgetFitExplainer : IBudgetFitExplainer
{
    public Task<Result<IReadOnlyList<BudgetFitChoice>>> ChooseAsync(string optionsText, string? requestText, CancellationToken cancellationToken)
    {
        var count = optionsText.Split('\n').Count(l => l.StartsWith('[') && l.Contains(']', StringComparison.Ordinal));
        IReadOnlyList<BudgetFitChoice> choices = Enumerable.Range(0, Math.Min(count, 3))
            .Select(i => new BudgetFitChoice(i, "週の予算に近づき、要望とも合います"))
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<BudgetFitChoice>>.Ok(choices));
    }
}

/// <summary>
/// E2E 用の偽の献立づくり。道具（本物の Biz）で候補を探し、日ごとに2品ずつ順に入れて submit_menu に出す。
/// 受け付けられなければ、問題の商品を外して出し直す（本物と同じく、検証は Biz が行う）。
/// チャットで「？」で終わる文章は質問として、submit_menu を出さずに Markdown の返答を少しずつ流す（D-91）。
/// </summary>
public sealed class FakeMenuPlannerAgent : IMenuPlannerAgent
{
    /// <summary>E2E の偽の質問への返答（Markdown）</summary>
    public const string AnswerText = "今週の献立について、お答えします（E2E の偽の返答）。\n\n- **しょっぱいもの・あまいもの**と、デザート・飲料を組み合わせています\n- 金額は、画面の週の実績で確かめてください\n\n献立は変えていません。";

    public async Task<Result<MenuAgentRunResult>> RunAsync(MenuAgentRequest request, IMenuAgentTools tools, IJobProgress progress, CancellationToken cancellationToken)
    {
        var text = request.RequestText?.Trim() ?? string.Empty;
        if (request.Mode == MenuAgentMode.Chat && (text.EndsWith('？') || text.EndsWith('?')))
        {
            await StreamAsync(AnswerText, progress, cancellationToken);
            return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(false, 1, AnswerText));
        }

        var result = Submit(request, tools);
        if (request.Mode == MenuAgentMode.Chat && tools.IsAccepted)
        {
            const string explanation = "ご依頼に合わせて、商品を組み直しました（E2E の偽の返答）。\n\n- **変えたところ**：日ごとの商品";
            await StreamAsync(explanation, progress, cancellationToken);
            return Result<MenuAgentRunResult>.Ok(result with { ReplyText = explanation });
        }

        return Result<MenuAgentRunResult>.Ok(result);
    }

    /// <summary>本物と同じく、文章を少しずつ報告する</summary>
    private static async Task StreamAsync(string text, IJobProgress progress, CancellationToken cancellationToken)
    {
        for (var length = 0; length < text.Length;)
        {
            length = Math.Min(text.Length, length + 8);
            progress.ReportText(text[..length]);
            await Task.Delay(40, cancellationToken);
        }
    }

    private static MenuAgentRunResult Submit(MenuAgentRequest request, IMenuAgentTools tools)
    {
        using var document = JsonDocument.Parse(tools.SearchProducts(null, null));
        var candidates = document.RootElement.GetProperty("products").EnumerateArray()
            .Select(p => (Id: p.GetProperty("id").GetString()!, SnackType: p.GetProperty("snackType").GetString()))
            .ToList();
        using var daysDocument = JsonDocument.Parse(tools.CheckBudget(new MenuAgentProposal([], [])));
        var dates = daysDocument.RootElement.GetProperty("days").EnumerateArray()
            .Where(d => d.GetProperty("dayType").GetString() == nameof(DayType.Normal))
            .Select(d => DateOnly.Parse(d.GetProperty("date").GetString()!))
            .ToList();

        var snacks = candidates.Where(c => c.SnackType != nameof(SnackType.Smooth)).Select(c => c.Id).ToList();
        var smooth = candidates.Where(c => c.SnackType == nameof(SnackType.Smooth)).Select(c => c.Id).ToList();
        var days = dates.Select((date, i) =>
        {
            var ids = new List<string>();
            if (snacks.Count > 0)
            {
                ids.Add(snacks[i % snacks.Count]);
            }

            if (smooth.Count > 0)
            {
                ids.Add(smooth[i % smooth.Count]);
            }

            return new MenuAgentProposalDay(date, ids, "しょっぱいもの・あまいものと、デザート・飲料を組み合わせました");
        }).ToList();

        // 本物と同じく check_budget で金額を確かめ、週の予算を超えるあいだは、後ろの日から2品目を外す（予算に少し届かない状態になる）
        for (var guard = 0; guard < days.Count; guard++)
        {
            using var budget = JsonDocument.Parse(tools.CheckBudget(new MenuAgentProposal(days, [])));
            if (budget.RootElement.GetProperty("total").GetInt32() <= budget.RootElement.GetProperty("budget").GetInt32())
            {
                break;
            }

            var index = days.FindLastIndex(d => d.ProductIds.Count > 1);
            if (index < 0)
            {
                break;
            }

            days[index] = days[index] with { ProductIds = days[index].ProductIds.Take(1).ToList() };
        }

        var interpreted = new InterpretedRequests();
        if (!string.IsNullOrWhiteSpace(request.RequestText) && request.Mode == MenuAgentMode.Create)
        {
            interpreted.Policies.Add(new InterpretedPolicy(request.RequestText.Trim(), null));
        }

        var explanation = request.Mode == MenuAgentMode.Chat
            ? "ご依頼に合わせて、商品を組み直しました（E2E の偽の返答）"
            : "候補の中から、分類が偏らないように選びました（E2E の偽の返答）";
        var iterations = 1;
        tools.SubmitMenu(new MenuAgentSubmission(new MenuAgentProposal(days, []), interpreted, [], explanation, false));
        return new MenuAgentRunResult(tools.IsAccepted, iterations);
    }
}
