using System.Text;
using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Claude.Prompts;

namespace OyatsuBiyori.Claude.Services;

/// <summary>画像を Claude に送る形にする（画像はログに出さない）</summary>
internal static class ImageBlocks
{
    public static BetaContentBlockParam From(ClaudeImage image) => new BetaImageBlockParam
    {
        Source = new BetaBase64ImageSource
        {
            Data = Convert.ToBase64String(image.Data),
            MediaType = image.MediaType switch
            {
                "image/png" => MediaType.ImagePng,
                "image/webp" => MediaType.ImageWebP,
                "image/gif" => MediaType.ImageGif,
                _ => MediaType.ImageJpeg,
            },
        },
    };
}

/// <summary>
/// チラシの読み取り（Sonnet・画像。T3-04）。誤りがある前提で扱い、アプリが検証してから使う。
/// 品目の対応づけはアプリでもう一度行う（FlyerAllergenPlanner.ParseLabel）。
/// </summary>
public sealed class FlyerReader(ClaudeRequestRunner runner) : IFlyerReader
{
    private readonly ClaudeRequestRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public async Task<Result<FlyerReading>> ReadAsync(IReadOnlyList<ClaudeImage> images, IReadOnlyList<Allergen> allergens, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        text.AppendLine("次の画像は、生協のチラシ（1枚または複数枚の写真・ページ）です。載っている商品をすべて読み取ってください。");
        text.AppendLine();
        text.AppendLine("アレルゲンの品目の一覧（allergenCodes にはこのコードを入れる）：");
        foreach (var allergen in allergens.OrderBy(a => a.SortOrder))
        {
            text.AppendLine($"- {allergen.Code}：{allergen.Name}");
        }

        text.AppendLine();
        text.AppendLine("味・種類のタグとして選べる語：" + string.Join("、", ProductTagVocabulary.Flavors));

        List<BetaContentBlockParam> content = [.. images.Select(ImageBlocks.From), new BetaTextBlockParam { Text = text.ToString() }];
        var result = await _runner.RunAsync<FlyerReading>(ClaudePurpose.FlyerReading, PromptStore.Get("FlyerReading"), content, cancellationToken);
        if (result.HasError)
        {
            return result;
        }

        // 味・種類のタグは決まった語だけを残す（D-61）
        var items = result.Value!.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => i with { FlavorTags = i.FlavorTags.Where(ProductTagVocabulary.IsFlavor).Distinct().ToList() })
            .ToList();
        return Result<FlyerReading>.Ok(new FlyerReading(items));
    }
}

/// <summary>
/// 読み取りの検証（別の呼び出し。T3-05）。疑わしい箇所・読み落とした商品・読み落としたアレルゲン表記だけを返させ、書き換えさせない。
/// </summary>
public sealed class FlyerReviewer(ClaudeRequestRunner runner) : IFlyerReviewer
{
    private readonly ClaudeRequestRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public async Task<Result<FlyerReview>> ReviewAsync(IReadOnlyList<ClaudeImage> images, FlyerReading reading, CancellationToken cancellationToken)
    {
        var listed = reading.Items.Select((item, index) => new
        {
            index,
            item.Name,
            item.Volume,
            item.PriceExcludingTax,
            item.PriceIncludingTax,
            item.OrderNo,
            item.AllergenLabelText,
            item.AllergenLabelAbsent,
        });
        var text = "次は、別の読み取りがこのチラシから読み取った結果です（index は番号）。チラシの画像と見比べて、読み違い・読み落としを探してください。\n"
            + JsonSerializer.Serialize(listed, JsonSchemas.SerializerOptions);
        List<BetaContentBlockParam> content = [.. images.Select(ImageBlocks.From), new BetaTextBlockParam { Text = text }];
        return await _runner.RunAsync<FlyerReview>(ClaudePurpose.FlyerReview, PromptStore.Get("FlyerReview"), content, cancellationToken);
    }
}

/// <summary>
/// 商品照合の判定（Haiku。T3-06）。候補は番号で選ばせ、商品 ID は渡さない（アプリが番号から商品に戻す）。
/// </summary>
public sealed class ProductMatchJudge(ClaudeRequestRunner runner) : IProductMatchJudge
{
    private readonly ClaudeRequestRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public async Task<Result<ProductMatchJudgement>> JudgeAsync(ProductMatchInput item, IReadOnlyList<Product> candidates, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        text.AppendLine($"チラシの商品：商品名「{item.Name}」・内容量「{item.Volume ?? "不明"}」・メーカー「{item.Maker ?? "不明"}」");
        text.AppendLine("登録済みの候補：");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            text.AppendLine($"{i + 1}. 商品名「{c.Name}」・内容量「{c.Volume ?? "不明"}」・メーカー「{c.Maker ?? "不明"}」");
        }

        var result = await _runner.RunAsync<MatchOutput>(ClaudePurpose.ProductMatch, PromptStore.Get("ProductMatch"), [new BetaTextBlockParam { Text = text.ToString() }], cancellationToken);
        if (result.HasError)
        {
            return Result<ProductMatchJudgement>.Error(result.ErrorMessages[0]);
        }

        var output = result.Value!;
        var verdict = output.Verdict;
        string? productId = null;
        if (verdict != ProductMatchVerdict.Different)
        {
            if (output.CandidateNumber is not int number || number < 1 || number > candidates.Count)
            {
                // 番号が候補にないときは、確認する側に倒して「別の商品」にする
                return Result<ProductMatchJudgement>.Ok(new ProductMatchJudgement(ProductMatchVerdict.Different, null, "候補の番号が読み取れなかったため、別の商品として扱います"));
            }

            productId = candidates[number - 1].Id;
        }

        return Result<ProductMatchJudgement>.Ok(new ProductMatchJudgement(verdict, productId, output.Reason));
    }

    /// <summary>判定の出力（番号は 1 から。別の商品なら null）</summary>
    internal sealed record MatchOutput(ProductMatchVerdict Verdict, int? CandidateNumber, string Reason);
}

/// <summary>
/// 予算に合わせる案の選択と説明（Sonnet。T4-09）。アプリが計算した候補の番号だけを選ばせる（FR-MENU-30）。
/// </summary>
public sealed class BudgetFitExplainer(ClaudeRequestRunner runner) : IBudgetFitExplainer
{
    private readonly ClaudeRequestRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public async Task<Result<IReadOnlyList<BudgetFitChoice>>> ChooseAsync(string optionsText, string? requestText, CancellationToken cancellationToken)
    {
        var text = "週の要望（データ）：" + (string.IsNullOrWhiteSpace(requestText) ? "（なし）" : requestText) + "\n\n" + optionsText;
        var result = await _runner.RunAsync<ChoiceOutput>(ClaudePurpose.BudgetFit, PromptStore.Get("BudgetFit"), [new BetaTextBlockParam { Text = text }], cancellationToken);
        if (result.HasError)
        {
            return Result<IReadOnlyList<BudgetFitChoice>>.Error(result.ErrorMessages[0]);
        }

        return Result<IReadOnlyList<BudgetFitChoice>>.Ok(result.Value!.Choices);
    }

    /// <summary>選んだ候補</summary>
    internal sealed record ChoiceOutput(List<BudgetFitChoice> Choices);
}
