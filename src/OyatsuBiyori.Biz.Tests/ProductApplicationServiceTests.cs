using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;
using OyatsuBiyori.Biz.Products;
using OyatsuBiyori.Biz.Tests.Fakes;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// 商品・アレルゲン確認の配線（T5-01〜06・T6-01〜02）。記録の規則（T1-19）がサービス経由でも効くこと、
/// 読み取りは候補でしかないこと、探す言葉の強調、ヘーゼルナッツの場面（T5-07）を確かめる。
/// </summary>
public class ProductApplicationServiceTests
{
    private sealed class FixedExtractor(AllergenExtraction extraction) : IAllergenExtractor
    {
        public Task<Result<AllergenExtraction>> ExtractAsync(IReadOnlyList<ClaudeImage> images, IReadOnlyList<Allergen> allergens, string productName, CancellationToken cancellationToken) =>
            Task.FromResult(Result<AllergenExtraction>.Ok(extraction));
    }

    private static ProductApplicationService Create(InMemoryData data, IAllergenExtractor? extractor = null, FakeJobs? jobs = null)
    {
        var time = new FixedTimeProvider(new DateTimeOffset(NoonUtc(Today)));
        var factory = new MenuContextFactory(new FakeProductRepository(data), new FakeSettingRepository(data), new FakeMenuRepository(data), time);
        return new ProductApplicationService(
            new FakeProductRepository(data),
            new FakeMenuRepository(data),
            new FakeSettingRepository(data),
            new FakeUnitOfWork(data),
            factory,
            jobs ?? new FakeJobs(),
            extractor ?? new FixedExtractor(new AllergenExtraction(string.Empty, [], null, [], [])),
            new FakeUserContext(),
            time);
    }

    private static AllergenCheckInput Input(Product product, AllergenCheckSource source, string? text, IEnumerable<AllergenItemDecision> items) =>
        new(product.Id, source, null, text, null, items.ToList(), [], []);

    private static BackgroundJobStatus Extracted(Product product, string rawIngredients, BackgroundJobState state = BackgroundJobState.Succeeded) =>
        new("job-1", ProductApplicationService.ExtractionJobKind, product.Id, state, null,
            new AllergenExtractionView(rawIngredients, [], null, [], []), [], NoonUtc(Today), NoonUtc(Today));

    private static IEnumerable<AllergenItemDecision> AllNotContains(InMemoryData data, params string[] contains) =>
        data.Allergens.Select(a => new AllergenItemDecision(a.Id, contains.Contains(a.Code)));

    [Fact]
    public async Task SR_12_パッケージでも全文が空なら_確認済みにできない()
    {
        var data = new InMemoryData();
        var product = Product("ことりのショコラ");
        data.Products.Add(product);
        var service = Create(data);

        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.Package, "  ", AllNotContains(data)));

        Assert.True(result.HasError);
        Assert.Empty(data.AddedChecks);
    }

    [Fact]
    public async Task SR_15_パッケージでは品目をすべて決めないと記録できない()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var product = Product("ことりのショコラ");
        data.Products.Add(product);
        var service = Create(data);

        var someOnly = data.Allergens.Where(a => a.Category == AllergenCategory.Specified).Select(a => new AllergenItemDecision(a.Id, false));
        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.Package, "小麦粉、砂糖", someOnly));

        Assert.True(result.HasError);
        Assert.Contains("決めていない品目", result.ErrorMessages[0]);
    }

    [Fact]
    public async Task SR_03_パッケージ以外の情報は参考になり_安全にはならない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var product = Product("ふわふわゼリー");
        data.Products.Add(product);
        var service = Create(data);

        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.CoopSite, null, AllNotContains(data)));

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        var check = Assert.Single(data.AddedChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.Empty(check.Items);
        Assert.Equal(AllergenJudgementCategory.Warning, result.Value!.Judgement.Category);
    }

    [Fact]
    public async Task T5_07_ヘーゼルナッツペーストが改行をまたいでも_含まないにしても使えないのまま()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var product = Product("ことりのさくさくショコラ", Reference(Today.AddDays(-3), "wheat", "milk", "soybean"));
        data.Products.Add(product);
        var menu = MenuTestData.NewMenu(20000, 50, (product, 321));
        product.PiecesPerPack = 8;
        MenuTestData.Put(menu, 2, product);
        data.Menus.Add(menu);
        var service = Create(data);
        const string text = "小麦粉（国内製造）、砂糖、ヘーゼルナッ\nツペースト、全粉乳（一部に小麦・乳成分・大豆を含む）";

        var hits = await service.FindKeywordHitsAsync(text);
        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.Package, text, AllNotContains(data, "wheat", "milk", "soybean")));

        // 「ヘーゼルナッツ」と「ヘーゼル」の両方の言葉が当たる。長い方は改行をまたいだ範囲で返る（強調のため）
        var hit = Assert.Single(hits.Value!, h => h.Keyword == "ヘーゼルナッツ");
        Assert.Equal("ヘーゼルナッ\nツ", text.Substring(hit.Start, hit.Length));
        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal(AllergenJudgementCategory.Blocked, result.Value!.Judgement.Category);
        Assert.Equal(AllergenJudgementKind.MayContain, result.Value.Judgement.Kind);
        var usage = Assert.Single(result.Value.Usages);
        Assert.Equal(MenuTestData.D(2), usage.Date);
    }

    [Fact]
    public async Task SR_14_職員が全文から言葉を消しても_サーバーの読み取り結果に言葉があれば使えないのまま()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var product = Product("ことりのさくさくショコラ");
        data.Products.Add(product);
        var jobs = new FakeJobs { Latest = Extracted(product, "小麦粉、砂糖、ヘーゼルナッ\nツペースト、全粉乳") };
        var service = Create(data, jobs: jobs);

        // 画面からは、言葉を消した全文だけが届く
        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.Package, "小麦粉、砂糖、全粉乳", AllNotContains(data, "wheat", "milk")));

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        var check = Assert.Single(data.AddedChecks);
        Assert.Equal("小麦粉、砂糖、ヘーゼルナッ\nツペースト、全粉乳", check.RawIngredientsOcr);
        Assert.Equal("小麦粉、砂糖、全粉乳", check.RawIngredients);
        Assert.Equal(AllergenJudgementCategory.Blocked, result.Value!.Judgement.Category);
        Assert.Equal(AllergenJudgementKind.MayContain, result.Value.Judgement.Kind);
    }

    [Fact]
    public async Task SR_14_読み取りが終わっていない_失敗した_別の商品の結果は_読み取った全文に入れない()
    {
        var data = new InMemoryData();
        var product = Product("ことりのショコラ");
        var other = Product("もぐもぐせんべい");
        data.Products.AddRange([product, other]);
        var failed = new FakeJobs { Latest = Extracted(product, "小麦粉", BackgroundJobState.Failed) };
        var otherProduct = new FakeJobs { Latest = Extracted(other, "米、しょうゆ") };

        await Create(data, jobs: failed).RecordCheckAsync(Input(product, AllergenCheckSource.Package, "小麦粉、砂糖", AllNotContains(data, "wheat")));
        await Create(data, jobs: otherProduct).RecordCheckAsync(Input(product, AllergenCheckSource.Package, "小麦粉、砂糖", AllNotContains(data, "wheat")));

        Assert.Equal(2, data.AddedChecks.Count);
        Assert.All(data.AddedChecks, c => Assert.Null(c.RawIngredientsOcr));
    }

    [Theory]
    [InlineData(BackgroundJobState.Queued)]
    [InlineData(BackgroundJobState.Running)]
    public async Task SR_14_読み取りの途中は_記録できない(BackgroundJobState state)
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var product = Product("ことりのショコラ");
        data.Products.Add(product);
        var jobs = new FakeJobs
        {
            Latest = Extracted(product, string.Empty, state),
            LatestSucceeded = Extracted(product, "小麦粉、ヘーゼルナッツペースト"),
        };
        var service = Create(data, jobs: jobs);

        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.Package, "小麦粉", AllNotContains(data, "wheat")));

        Assert.True(result.HasError);
        Assert.Contains("読み取っている途中", result.ErrorMessages[0]);
        Assert.Empty(data.AddedChecks);
    }

    [Fact]
    public async Task SR_14_読み取り直しが失敗しても_前に終わった読み取りの全文で判定する()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var product = Product("ことりのショコラ");
        data.Products.Add(product);
        var jobs = new FakeJobs
        {
            Latest = Extracted(product, string.Empty, BackgroundJobState.Failed),
            LatestSucceeded = Extracted(product, "小麦粉、ヘーゼルナッツペースト"),
        };
        var service = Create(data, jobs: jobs);

        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.Package, "小麦粉", AllNotContains(data, "wheat")));

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal("小麦粉、ヘーゼルナッツペースト", Assert.Single(data.AddedChecks).RawIngredientsOcr);
        Assert.Equal(AllergenJudgementCategory.Blocked, result.Value!.Judgement.Category);
    }

    [Fact]
    public async Task SR_12_読み取りがないとき_手で入れた全文だけで確認済みにでき_読み取った全文は空()
    {
        var data = new InMemoryData();
        var product = Product("もぐもぐせんべい");
        data.Products.Add(product);
        var service = Create(data);

        var result = await service.RecordCheckAsync(Input(product, AllergenCheckSource.Package, "うるち米（国産）、しょうゆ（小麦・大豆を含む）", AllNotContains(data, "wheat", "soybean")));

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        var check = Assert.Single(data.AddedChecks);
        Assert.Null(check.RawIngredientsOcr);
        Assert.Equal(AllergenCheckStatus.Confirmed, check.Status);
    }

    [Fact]
    public async Task SR_14_商品名で当たった避ける品目の言葉は_名前を変えて消せない()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var product = Product("ヘーゼルナッツのクッキー");
        data.Products.Add(product);
        var service = Create(data);

        var removed = await service.UpdateAsync(new ProductUpdateInput(product.Id, "ナッツなしクッキー", null, null, 10, 1, 200, null, SnackType.Sweet, null, false, [], []));
        var kept = await service.UpdateAsync(new ProductUpdateInput(product.Id, "ヘーゼルナッツのクッキー（大袋）", null, null, 10, 1, 200, null, SnackType.Sweet, null, false, [], []));

        Assert.True(removed.HasError);
        Assert.Contains("ヘーゼルナッツ", removed.ErrorMessages[0]);
        Assert.False(kept.HasError, string.Join(",", kept.ErrorMessages));
        Assert.Equal("ヘーゼルナッツのクッキー（大袋）", product.Name);
    }

    [Fact]
    public async Task JAN_コードは全角で入れても半角にそろえる()
    {
        var data = new InMemoryData();
        var product = Product("さくさくビスケット");
        data.Products.Add(product);
        var service = Create(data);

        var result = await service.UpdateAsync(new ProductUpdateInput(product.Id, product.Name, null, null, 10, 1, 200, "４９０１２３４５６７８９４", SnackType.Sweet, null, false, [], []));

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal("4901234567894", product.JanCode);
    }

    [Fact]
    public async Task 読み取りは候補だけを返し_記録は足さない_画像は破棄する()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var product = Product("ことりのショコラ");
        data.Products.Add(product);
        var service = Create(data, new FixedExtractor(new AllergenExtraction("小麦粉、ヘーゼルナッツペースト", ["wheat", "hazelnut", "pecan", "unknown"], "同じ工場でくるみを使っています", ["ことり"], ["チョコ", "知らない語"])));
        var images = new List<ClaudeImage> { new([1], "image/jpeg") };

        var result = await service.RunExtractionAsync(product.Id, images, new NullProgress(), CancellationToken.None);

        Assert.Empty(images);
        Assert.Empty(data.AddedChecks);
        var ids = result.Value!.ContainsAllergenIds;
        Assert.Contains(Allergen("wheat").Id, ids);
        Assert.Contains(Allergen("hazelnut").Id, ids);
        // 避ける品目にない施設の追加品目・知らない品目は候補にしない
        Assert.DoesNotContain(Allergen("pecan").Id, ids);
        Assert.Equal(["チョコ"], result.Value.FlavorTags);
    }

    [Fact]
    public async Task JAN_コードがほかの商品で使われていれば_保存しない()
    {
        var data = new InMemoryData();
        var a = Product("さくさくビスケット");
        a.JanCode = "4901234567894";
        var b = Product("もぐもぐせんべい");
        data.Products.AddRange([a, b]);
        var service = Create(data);

        var result = await service.UpdateAsync(new ProductUpdateInput(b.Id, b.Name, null, null, 10, 1, 200, "4901234567894", SnackType.Salty, null, false, [], []));

        Assert.True(result.HasError);
        Assert.Contains("さくさくビスケット", result.ErrorMessages[0]);
        Assert.Null(b.JanCode);
    }

    [Fact]
    public async Task 商品名を変えたら正規化名を作り直し_味のタグは決まった語だけ()
    {
        var data = new InMemoryData();
        var product = Product("さくさくビスケット");
        data.Products.Add(product);
        var service = Create(data);

        var bad = await service.UpdateAsync(new ProductUpdateInput(product.Id, "コープ さくさくクッキー", null, null, 10, 1, 200, null, SnackType.Sweet, null, false, [], ["知らない語"]));
        var ok = await service.UpdateAsync(new ProductUpdateInput(product.Id, "コープ さくさくクッキー", null, null, 10, 1, 200, null, SnackType.Sweet, "子どもに人気", false, ["ことり"], ["焼き菓子"]));

        Assert.True(bad.HasError);
        Assert.False(ok.HasError);
        Assert.Equal(TextNormalizer.ForProductMatch("コープ さくさくクッキー"), product.NormalizedName);
        Assert.Contains(product.Tags, t => t.Kind == ProductTagKind.Flavor && t.Value == "焼き菓子");
    }

    [Fact]
    public async Task 一覧は表示で絞り込み_使わない商品は既定で隠す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var unknown = Product("なにもないクッキー");
        var safe = Product("さくさくビスケット", ConfirmedAll(Today.AddDays(-5), []));
        var disabled = Product("つかわないゼリー", ConfirmedAll(Today.AddDays(-5), []));
        disabled.IsDisabled = true;
        data.Products.AddRange([unknown, safe, disabled]);
        var service = Create(data);

        var blocked = await service.ListAsync(null, ProductListFilter.Blocked, false);
        var all = await service.ListAsync(null, ProductListFilter.All, false);
        var withDisabled = await service.ListAsync(null, ProductListFilter.All, true);
        var search = await service.ListAsync("ビスケット", ProductListFilter.All, false);

        Assert.Equal([unknown.Id], blocked.Value!.Items.Select(i => i.Id));
        Assert.DoesNotContain(all.Value!.Items, i => i.Id == disabled.Id);
        Assert.Contains(withDisabled.Value!.Items, i => i.Id == disabled.Id);
        Assert.Equal([safe.Id], search.Value!.Items.Select(i => i.Id));
    }
}
