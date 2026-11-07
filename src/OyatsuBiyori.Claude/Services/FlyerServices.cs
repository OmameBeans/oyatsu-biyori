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

        // 味・種類のタグは決まった語だけを残す（D-61）。商品名が読めなかった商品も黙って消さず、再入力にするため残す（09 の 3.1）。
        // 何も読めていない行（商品名・メーカー・価格・注文番号・内容量がどれもない）だけは、商品ではないものとして除く。
        // 食品以外の印は、理由がなければ「理由の記載なし」を入れる（外した掲載品に理由を必ず出す。FR-IMPORT-20）
        var items = result.Value!.Items
            .Where(HasAnyContent)
            .Select(i => i with
            {
                Name = i.Name?.Trim() ?? string.Empty,
                FlavorTags = i.FlavorTags.Where(ProductTagVocabulary.IsFlavor).Distinct().ToList(),
                NonFoodReason = i.IsNonFood ? (string.IsNullOrWhiteSpace(i.NonFoodReason) ? "理由の記載なし" : i.NonFoodReason.Trim()) : null,
            })
            .ToList();
        return Result<FlyerReading>.Ok(new FlyerReading(items));
    }

    private static bool HasAnyContent(FlyerReadProduct item) =>
        !string.IsNullOrWhiteSpace(item.Name)
        || !string.IsNullOrWhiteSpace(item.Maker)
        || !string.IsNullOrWhiteSpace(item.Volume)
        || !string.IsNullOrWhiteSpace(item.OrderNo)
        || item.PriceIncludingTax is not null
        || item.PriceExcludingTax is not null;
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
            item.Maker,
            item.Volume,
            item.PriceExcludingTax,
            item.PriceIncludingTax,
            item.OrderNo,
            item.AllergenLabelText,
            item.AllergenLabelAbsent,
            item.PiecesPerPackCandidate,
            item.IsNonFood,
        });
        var text = "次は、別の読み取りがこのチラシから読み取った結果です（index は番号）。チラシの画像と見比べて、読み違い・読み落としを探してください。\n"
            + JsonSerializer.Serialize(listed, JsonSchemas.SerializerOptions);
        List<BetaContentBlockParam> content = [.. images.Select(ImageBlocks.From), new BetaTextBlockParam { Text = text }];
        return await _runner.RunAsync<FlyerReview>(ClaudePurpose.FlyerReview, PromptStore.Get("FlyerReview"), content, cancellationToken);
    }
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
