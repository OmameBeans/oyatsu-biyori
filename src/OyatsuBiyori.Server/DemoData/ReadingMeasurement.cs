using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;
using OyatsuBiyori.Server.E2E;

namespace OyatsuBiyori.Server.DemoData;

/// <summary>
/// 読み取り精度の測定（T3-11）と、1回分の API の使用量（T7-02 の材料）。
/// samples/ の架空のチラシ・パッケージを本物の Claude で読み取り、正解データ（*.expected.json）と比べて Markdown で出す。
/// 開発の環境・開発の DB でだけ動く（DemoDataGuard）。実際のチラシ・パッケージは使わない。
/// </summary>
public static class ReadingMeasurement
{
    /// <summary>1回の読み取りで送る画像の組（チラシの PDF は画面で画像にしてから送るので、ここでは測らない）</summary>
    private static readonly string[][] FlyerImageSets =
    [
        ["flyer-01.png"],
        ["flyer-01-photo-top.png", "flyer-01-photo-bottom.png"],
    ];

    /// <summary>測って、結果の Markdown を返す</summary>
    public static async Task<string> MeasureReadingAsync(this IServiceProvider services, IHostEnvironment environment, IConfiguration configuration, CancellationToken cancellationToken)
    {
        DemoDataGuard.EnsureAllowed(environment, configuration);

        // E2E の環境では偽の Claude（正解データをそのまま返す）で動き、比べるコードが全問正解を出すかの自己確認になる
        var isSelfCheck = E2EEnvironment.IsE2E(environment);
        if (!isSelfCheck && string.IsNullOrWhiteSpace(configuration["Claude:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            throw new InvalidOperationException("API キーがありません。dotnet user-secrets set \"Claude:ApiKey\" \"<キー>\" --project src/OyatsuBiyori.Server で設定してください");
        }

        var samples = FindSamplesDirectory(environment.ContentRootPath);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<OyatsuBiyoriDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        var timeProvider = provider.GetRequiredService<TimeProvider>();
        var startedAt = timeProvider.GetUtcNow().UtcDateTime;

        var allergens = await provider.GetRequiredService<ISettingRepository>().GetAllergensAsync();
        var report = new StringBuilder();
        report.AppendLine($"# 読み取り精度の測定（{JapanDate.Today(timeProvider):yyyy-MM-dd}）");
        report.AppendLine();
        report.AppendLine(isSelfCheck
            ? "**偽の Claude での自己確認（E2E の環境）**。正解データをそのまま返すので、全問正解にならなければ比べるコードの誤り。精度の測定ではない。"
            : "架空のチラシ・パッケージ（samples/）を本物の Claude で読み取り、正解データと比べた結果。モデル・効き具合は appsettings.json の設定のまま。");
        report.AppendLine();

        await MeasureFlyersAsync(provider, Path.Combine(samples, "flyers"), allergens, report, cancellationToken);
        await MeasurePackagesAsync(provider, samples, allergens, report, cancellationToken);
        await AppendUsageAsync(context, startedAt, report, cancellationToken);
        return report.ToString();
    }

    private static async Task MeasureFlyersAsync(IServiceProvider provider, string directory, IReadOnlyList<Allergen> allergens, StringBuilder report, CancellationToken cancellationToken)
    {
        var reader = provider.GetRequiredService<IFlyerReader>();
        var reviewer = provider.GetRequiredService<IFlyerReviewer>();
        using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "flyer-01.expected.json"), cancellationToken));
        var items = expected.RootElement.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!);
        var codesByImage = expected.RootElement.GetProperty("images").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(c => c.GetString()!).ToList());

        report.AppendLine("## チラシ");
        report.AppendLine();
        foreach (var set in FlyerImageSets)
        {
            var images = new List<ClaudeImage>();
            foreach (var file in set)
            {
                images.Add(new ClaudeImage(await File.ReadAllBytesAsync(Path.Combine(directory, file), cancellationToken), "image/png"));
            }

            var expectedItems = set.SelectMany(f => codesByImage[f]).Distinct().Select(c => items[c]).ToList();
            report.AppendLine($"### {string.Join(" ＋ ", set)}（正解 {expectedItems.Count} 商品）");
            report.AppendLine();
            var reading = await reader.ReadAsync(images, allergens, cancellationToken);
            if (reading.HasError)
            {
                report.AppendLine($"読み取りに失敗しました：{string.Join(" / ", reading.ErrorMessages)}");
                report.AppendLine();
                continue;
            }

            var read = reading.Value!.Items.ToList();
            var pairs = MatchByName(expectedItems, read);
            var nameOk = 0;
            var nameLooseOk = 0;
            var priceOk = 0;
            var allergenOk = 0;
            var orderOk = 0;
            var rows = new List<string>();
            foreach (var item in expectedItems)
            {
                var name = item.GetProperty("name").GetString()!;
                if (!pairs.TryGetValue(item.GetProperty("code").GetString()!, out var product))
                {
                    rows.Add($"| {item.GetProperty("code").GetString()} | {name} | 読み落とし | - | - | - |");
                    continue;
                }

                // 商品名は、厳密（NFKC と空白だけをそろえる。「CO・OP」の読み落としやカナの崩れは誤り）と、
                // 同じ商品かどうかの判定に使う緩い比べ方（「コープ」・記号を除く）の両方で数える
                var isName = Strict(product.Name) == Strict(name);
                var isNameLoose = TextNormalizer.ForProductMatch(product.Name) == TextNormalizer.ForProductMatch(name);
                var isPrice = product.PriceIncludingTax == IntOrNull(item, "priceIncludingTax") && product.PriceExcludingTax == IntOrNull(item, "priceExcludingTax");
                var isOrder = TextNormalizer.ForKeywordSearch(product.OrderNo) == TextNormalizer.ForKeywordSearch(item.GetProperty("code").GetString());

                // アレルゲン表記：品目が一致し、対応づけできない語がなく、表記がない商品は「表記なし」と読めていること
                var expectedCodes = LabelCodes(item, allergens, out var unmapped);
                var isAllergen = unmapped.Count == 0
                    && product.UnmappedAllergenTerms.Count == 0
                    && expectedCodes.SetEquals(product.AllergenCodes)
                    && (expectedCodes.Count == 0
                        ? product.AllergenLabelAbsent && string.IsNullOrWhiteSpace(product.AllergenLabelText)
                        : !product.AllergenLabelAbsent);
                var allergenRead = product.AllergenLabelAbsent
                    ? "表記なし"
                    : $"{product.AllergenLabelText}（{string.Join("・", product.AllergenCodes)}{(product.UnmappedAllergenTerms.Count > 0 ? "／対応づけできない語：" + string.Join("・", product.UnmappedAllergenTerms) : string.Empty)}）";
                nameOk += isName ? 1 : 0;
                nameLooseOk += isNameLoose ? 1 : 0;
                priceOk += isPrice ? 1 : 0;
                allergenOk += isAllergen ? 1 : 0;
                orderOk += isOrder ? 1 : 0;
                rows.Add($"| {item.GetProperty("code").GetString()} | {name} | {Mark(isName, product.Name)} | {Mark(isPrice, $"{product.PriceExcludingTax}/{product.PriceIncludingTax}")} | {Mark(isAllergen, allergenRead)} | {Mark(isOrder, product.OrderNo)} |");
            }

            var extra = read.Where(r => !pairs.Values.Any(p => ReferenceEquals(p, r))).Select(p => p.Name).ToList();
            report.AppendLine($"- 見つけた商品：{pairs.Count} / {expectedItems.Count}（正解にない商品：{extra.Count} 件{(extra.Count > 0 ? "：" + string.Join("・", extra) : string.Empty)}）");
            report.AppendLine($"- 商品名：{nameOk} / {expectedItems.Count}（「CO・OP」・記号の違いを除けば {nameLooseOk} / {expectedItems.Count}）、価格（税抜・税込）：{priceOk} / {expectedItems.Count}、アレルゲン表記：{allergenOk} / {expectedItems.Count}、注文番号：{orderOk} / {expectedItems.Count}");

            var review = await reviewer.ReviewAsync(images, reading.Value, cancellationToken);
            report.AppendLine(review.HasError
                ? $"- 検証：失敗（{string.Join(" / ", review.ErrorMessages)}）"
                : $"- 検証：疑わしい箇所 {review.Value!.Notes.Count} 件、読み落とした商品 {review.Value.MissedProducts.Count} 件、読み落としたアレルゲン表記 {review.Value.MissedAllergenLabels.Count} 件");
            report.AppendLine();
            report.AppendLine("| 番号 | 正解の商品名 | 商品名 | 価格 | アレルゲン表記 | 注文番号 |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- |");
            rows.ForEach(r => report.AppendLine(r));
            report.AppendLine();
            images.Clear();
        }
    }

    private static async Task MeasurePackagesAsync(IServiceProvider provider, string samples, IReadOnlyList<Allergen> allergens, StringBuilder report, CancellationToken cancellationToken)
    {
        var extractor = provider.GetRequiredService<IAllergenExtractor>();
        var directory = Path.Combine(samples, "packages");

        // 本番と同じく、商品名を添えて読み取らせる（商品名はチラシの正解データから取る）
        using var flyer = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(samples, "flyers", "flyer-01.expected.json"), cancellationToken));
        var namesByCode = flyer.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("name").GetString()!);
        report.AppendLine("## パッケージ（原材料表示）");
        report.AppendLine();
        report.AppendLine("| パッケージ | 全文の一致（正規化後の Dice 係数） | アレルゲン表示（不足がないか） | 表示より多く拾った品目 | 全文で探す言葉が当たったか |");
        report.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var path in Directory.GetFiles(directory, "package-*.expected.json").Order(StringComparer.Ordinal))
        {
            using var expected = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            var root = expected.RootElement;
            var images = new List<ClaudeImage>();
            foreach (var file in root.GetProperty("images").EnumerateArray())
            {
                images.Add(new ClaudeImage(await File.ReadAllBytesAsync(Path.Combine(directory, file.GetString()!), cancellationToken), "image/png"));
            }

            var label = Path.GetFileName(path).Replace(".expected.json", string.Empty, StringComparison.Ordinal);
            var productName = namesByCode.GetValueOrDefault(root.GetProperty("flyerCode").GetString() ?? string.Empty, label);
            var result = await extractor.ExtractAsync(images, allergens, productName, cancellationToken);
            images.Clear();
            if (result.HasError)
            {
                report.AppendLine($"| {label}（{productName}） | 失敗：{string.Join(" / ", result.ErrorMessages)} | - | - | - |");
                continue;
            }

            var extraction = result.Value!;
            var dice = ProductMatcher.DiceCoefficient(
                TextNormalizer.ForKeywordSearch(extraction.RawIngredients),
                TextNormalizer.ForKeywordSearch(root.GetProperty("ingredientsText").GetString()));
            var expectedCodes = NamesToCodes(root.GetProperty("labelAllergens").EnumerateArray().Select(e => e.GetString()!), allergens, out var unmapped);
            // アレルゲン表示（29品目）に不足がないかを見る。プロンプトは原材料名に書かれた品目も拾わせるので、
            // 表示より多いのは誤りとせず別の列に出す。施設の追加品目は全文から探すもので、表示には出ない
            var labelRead = extraction.ContainsAllergenCodes
                .Where(c => allergens.Any(a => a.Code == c && a.Category != AllergenCategory.FacilityAdded))
                .ToHashSet(StringComparer.Ordinal);
            var isLabel = unmapped.Count == 0 && labelRead.IsSupersetOf(expectedCodes);
            var extraLabel = labelRead.Except(expectedCodes).ToList();

            // アプリの探す言葉が、読み取った全文で当たるか（ヘーゼルナッツの改行など。判定はアプリが行う）
            var hits = AllergenKeywordFinder.Find(extraction.RawIngredients, allergens);
            var hitNames = hits.Select(h => allergens.First(a => a.Id == h.AllergenId).Name).Concat(hits.Select(h => h.Keyword)).ToHashSet(StringComparer.Ordinal);
            var found = root.GetProperty("foundInIngredients").EnumerateArray().Select(e => e.GetString()!).ToList();
            var missed = found.Where(f => !hitNames.Contains(f)).ToList();
            report.AppendLine($"| {label}（{productName}） | {dice:0.00} | {Mark(isLabel, string.Join("・", extraction.ContainsAllergenCodes))} | {(extraLabel.Count == 0 ? "なし" : string.Join("・", extraLabel))} | {(found.Count == 0 ? "（対象なし）" : missed.Count == 0 ? "✅ " + string.Join("・", found) : "❌ 見落とし：" + string.Join("・", missed))} |");
        }

        report.AppendLine();
    }

    private static async Task AppendUsageAsync(OyatsuBiyoriDbContext context, DateTime startedAt, StringBuilder report, CancellationToken cancellationToken)
    {
        var logs = await context.ApiUsageLogs.AsNoTracking().Where(l => l.CalledAt >= startedAt).ToListAsync(cancellationToken);
        report.AppendLine("## この測定で使った API（T7-02 の材料。料金は単価と為替の日付を添えて別に計算する）");
        report.AppendLine();
        report.AppendLine("| 用途 | モデル | 回数 | 入力 | 出力 | キャッシュ書き込み | キャッシュ読み込み |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var group in logs.GroupBy(l => (l.Purpose, l.Model)).OrderBy(g => g.Key.Purpose))
        {
            report.AppendLine($"| {group.Key.Purpose} | {group.Key.Model} | {group.Count()} | {group.Sum(l => l.InputTokens)} | {group.Sum(l => l.OutputTokens)} | {group.Sum(l => l.CacheCreationInputTokens)} | {group.Sum(l => l.CacheReadInputTokens)} |");
        }

        report.AppendLine();
    }

    /// <summary>正解の商品と読み取った商品を、商品名の似ている順に1対1で結びつける（しきい値 0.5）</summary>
    private static Dictionary<string, FlyerReadProduct> MatchByName(IReadOnlyList<JsonElement> expected, IReadOnlyList<FlyerReadProduct> read)
    {
        var candidates = expected
            .SelectMany(e => read.Select(r => (Expected: e, Read: r, Score: ProductMatcher.DiceCoefficient(
                TextNormalizer.ForProductMatch(e.GetProperty("name").GetString()), TextNormalizer.ForProductMatch(r.Name)))))
            .Where(c => c.Score >= 0.5)
            .OrderByDescending(c => c.Score);
        var pairs = new Dictionary<string, FlyerReadProduct>(StringComparer.Ordinal);
        var used = new HashSet<FlyerReadProduct>(ReferenceEqualityComparer.Instance);
        foreach (var (e, r, _) in candidates)
        {
            var code = e.GetProperty("code").GetString()!;
            if (!pairs.ContainsKey(code) && !used.Contains(r))
            {
                pairs[code] = r;
                used.Add(r);
            }
        }

        return pairs;
    }

    private static HashSet<string> LabelCodes(JsonElement item, IReadOnlyList<Allergen> allergens, out List<string> unmapped)
    {
        var names = item.TryGetProperty("allergenLabel", out var label) && label.ValueKind == JsonValueKind.Array
            ? label.EnumerateArray().Select(e => e.GetString()!)
            : [];
        return NamesToCodes(names, allergens, out unmapped);
    }

    private static HashSet<string> NamesToCodes(IEnumerable<string> names, IReadOnlyList<Allergen> allergens, out List<string> unmapped)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        unmapped = [];
        foreach (var name in names)
        {
            var allergen = allergens.FirstOrDefault(a => a.Name == name)
                ?? allergens.FirstOrDefault(a => a.Keywords.Any(k => TextNormalizer.ForKeywordSearch(k.Keyword) == TextNormalizer.ForKeywordSearch(name)));
            if (allergen is null)
            {
                unmapped.Add(name);
            }
            else
            {
                codes.Add(allergen.Code);
            }
        }

        return codes;
    }

    private static int? IntOrNull(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string Mark(bool isOk, string? read) => isOk ? "✅" : $"❌ {read}";

    private static string Strict(string? text) =>
        new string((text ?? string.Empty).Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string FindSamplesDirectory(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "samples");
            if (Directory.Exists(Path.Combine(candidate, "flyers")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("samples/flyers が見つかりません。リポジトリの中で動かしてください");
    }
}
