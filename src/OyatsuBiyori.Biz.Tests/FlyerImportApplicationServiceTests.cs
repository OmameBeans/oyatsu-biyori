using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Tests.Fakes;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// チラシの取り込みの配線（T3-07・T3-08）。どの商品の記録を Plan に渡すか・「同じ商品」に何を選べるか・参考の記録を足すかを、
/// メモリ上のリポジトリで確かめる（SR-05・SR-19・SR-20）。
/// </summary>
public class FlyerImportApplicationServiceTests
{
    private static FlyerReadProduct Read(string name, string? volume, string? label = null, string? description = null, IReadOnlyList<string>? unmapped = null) => new(
        name, null, volume, 200, 216, "8101", null, label, label is null, [], unmapped ?? [], description, null, 10, "10袋入りのため", SnackType.Sweet, [], []);

    private static (FlyerImportApplicationService Service, InMemoryData Data, Menu Menu) Create(
        InMemoryData data,
        IFlyerReader reader,
        IFlyerReviewer? reviewer = null,
        ProductMatchVerdict verdict = ProductMatchVerdict.Different)
    {
        var menu = MenuTestData.NewMenu(20000, 50);
        data.Menus.Add(menu);
        var service = new FlyerImportApplicationService(
            new FakeMenuRepository(data),
            new FakeProductRepository(data),
            new FakeSettingRepository(data),
            new FakeUnitOfWork(data),
            new FakeSessionStore(),
            new FakeJobs(),
            reader,
            reviewer ?? new FixedFlyerReviewer(new FlyerReview([], [], [])),
            new FixedJudge(verdict),
            new FakeUserContext(),
            new FixedTimeProvider(new DateTimeOffset(NoonUtc(Today))),
            new ProductMatchOptions(),
            new FlyerImportOptions());
        return (service, data, menu);
    }

    private static async Task<FlyerItemView> ReadSingleAsync(FlyerImportApplicationService service, Menu menu)
    {
        var read = await service.RunReadingAsync(menu.Id, [new ClaudeImage([1, 2, 3], "image/jpeg")], new NullProgress(), CancellationToken.None);
        Assert.False(read.HasError, string.Join(",", read.ErrorMessages));
        var view = await service.GetLatestAsync(menu.Id);
        return Assert.Single(view.Value!.Session!.Items);
    }

    private static FlyerConfirmInput Input(Menu menu, FlyerItemView item, FlyerItemChoice choice, string? productId = null, IReadOnlyList<string>? labelIds = null, IReadOnlyList<string>? resolved = null) => new(
        menu.Id, item.Id, choice, productId, item.Read.Name, item.Read.Maker, item.Read.Volume, item.Read.PiecesPerPackCandidate, false, item.Read.SnackType, false,
        item.Read.PriceIncludingTax, item.Read.PriceExcludingTax, item.Read.OrderNo, null, [], [], labelIds ?? item.LabelAllergens.Select(a => a.Id).ToList(), resolved ?? [], false);

    [Fact]
    public async Task 画像は読み取りと検証が終わったら破棄する()
    {
        var data = new InMemoryData();
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("もぐもぐクッキー", "60g")));
        var images = new List<ClaudeImage> { new([1, 2, 3], "image/jpeg") };

        await service.RunReadingAsync(menu.Id, images, new NullProgress(), CancellationToken.None);

        Assert.Empty(images);
    }

    [Fact]
    public async Task SR_05_内容量違いの商品は_同じ商品として確定できない()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var existing = Product("もぐもぐクッキー", ConfirmedAll(Today.AddDays(-5), []));
        existing.Volume = "120g";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("もぐもぐクッキー", "60g")));

        var item = await ReadSingleAsync(service, menu);
        var result = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.Same, existing.Id));

        Assert.Contains(existing.Id, item.VolumeVariants.Select(v => v.ProductId));
        Assert.DoesNotContain(existing.Id, item.SameChoiceProductIds);
        Assert.True(result.HasError);
        Assert.Empty(menu.ListedProducts);
    }

    [Fact]
    public async Task SR_05_味違いと判定された似ている商品も_同じ商品として確定できない()
    {
        var data = new InMemoryData();
        var existing = Product("ぽりぽり スナック うすしお", ConfirmedAll(Today.AddDays(-5), []));
        existing.Volume = "18g×6袋";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("ぽりぽり スナック のりしお", "18g×6袋")), verdict: ProductMatchVerdict.FlavorVariant);

        var item = await ReadSingleAsync(service, menu);
        var result = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.Same, existing.Id));

        Assert.NotNull(item.Judgement);
        Assert.Empty(item.SameChoiceProductIds);
        Assert.True(result.HasError);
    }

    [Fact]
    public async Task AI_が同じ商品と判定した候補は_同じ商品として選べる()
    {
        var data = new InMemoryData();
        var existing = Product("ぽりぽり スナック うすしお 6袋", ConfirmedAll(Today.AddDays(-5), []));
        existing.Volume = "18g×6袋";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("ぽりぽり スナック うすしお", "18g×6袋")), verdict: ProductMatchVerdict.Same);

        var item = await ReadSingleAsync(service, menu);

        Assert.Contains(existing.Id, item.SameChoiceProductIds);
    }

    [Fact]
    public async Task SR_05_味違い内容量違いとして登録すると_別の商品になり確認を引き継がない()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var existing = Product("もぐもぐクッキー", ConfirmedAll(Today.AddDays(-5), []));
        existing.Volume = "120g";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("もぐもぐクッキー", "60g")));

        var item = await ReadSingleAsync(service, menu);
        var result = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.Variant));

        Assert.False(result.HasError);
        var created = Assert.Single(data.Products, p => p.Id != existing.Id);
        var check = Assert.Single(created.AllergenChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.Equal(AllergenCheckSource.Flyer, check.Source);
        Assert.Equal(created.Id, Assert.Single(menu.ListedProducts).ProductId);
        Assert.Equal(AllergenJudgementCategory.Warning, AllergenJudge.Judge(created, data.Avoided.Select(a => a.Allergen), Today, RecheckDays).Category);
    }

    [Fact]
    public async Task 確認済みの商品に同じ商品で取り込んでも_参考の記録は増えない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Product("さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.Volume = "10袋入";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", "10袋入", "小麦")));

        var item = await ReadSingleAsync(service, menu);
        var result = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.Same, existing.Id));

        Assert.False(result.HasError);
        Assert.Single(existing.AllergenChecks);
        Assert.Empty(data.AddedChecks);
    }

    [Fact]
    public async Task SR_20_確認済みの商品でチラシに避ける品目があれば_参考の記録が足され使えなくなる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Product("さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.Volume = "10袋入";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", "10袋入", "卵・小麦")));

        var item = await ReadSingleAsync(service, menu);
        Assert.False(item.CanBulkConfirm);
        var result = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.Same, existing.Id));

        Assert.False(result.HasError);
        Assert.Equal(2, existing.AllergenChecks.Count);
        Assert.Equal(AllergenJudgementCategory.Blocked, AllergenJudge.Judge(existing, data.Avoided.Select(a => a.Allergen), Today, RecheckDays).Category);
    }

    [Fact]
    public async Task SR_20_食い違いのある商品は_まとめて確定しない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Product("さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.Volume = "10袋入";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", "10袋入", "卵・小麦")));

        await ReadSingleAsync(service, menu);
        var count = await service.ConfirmAllSameAsync(menu.Id);

        Assert.Equal(0, count.Value);
        Assert.Empty(menu.ListedProducts);
    }

    [Fact]
    public async Task 対応づけできない表記の語が残っていると_確定できない()
    {
        var data = new InMemoryData();
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("ふしぎなクッキー", "5袋", "小麦・ナゾ成分", unmapped: ["ナゾ成分"])));

        var item = await ReadSingleAsync(service, menu);
        var blocked = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.New));
        var resolved = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.New, resolved: item.UnmappedTerms));

        Assert.Contains("ナゾ成分", item.UnmappedTerms);
        Assert.True(blocked.HasError);
        Assert.False(resolved.HasError);
    }

    [Fact]
    public async Task SR_19_読み取りの検証は表記の品目を足すだけで_減らさない()
    {
        var data = new InMemoryData();
        var review = new FlyerReview([], [], [new FlyerMissedAllergenLabel(0, "乳", "説明文の末尾に（乳）があります")]);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("のむヨーグルト", "125ml×6", "小麦")), new FixedFlyerReviewer(review));

        var item = await ReadSingleAsync(service, menu);

        Assert.Contains(item.LabelAllergens, a => a.Name == "乳");
        Assert.Contains(item.LabelAllergens, a => a.Name == "小麦");
        Assert.Contains(item.Issues, i => i.Source == "review" && i.Field == "アレルゲン");
    }

    [Fact]
    public async Task 読み取りの検証に失敗しても_読み取り結果は出す()
    {
        var data = new InMemoryData();
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("のむヨーグルト", "125ml×6", "乳")), new FixedFlyerReviewer(null));

        await service.RunReadingAsync(menu.Id, [new ClaudeImage([1], "image/jpeg")], new NullProgress(), CancellationToken.None);
        var view = await service.GetLatestAsync(menu.Id);

        Assert.True(view.Value!.Session!.ReviewFailed);
        Assert.Single(view.Value.Session.Items);
    }

    [Fact]
    public async Task 職員が外した表記の品目は_参考の記録に残る()
    {
        var data = new InMemoryData();
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("たまごボーロ", "18g×4袋", "卵・乳")));

        var item = await ReadSingleAsync(service, menu);
        var milkOnly = item.LabelAllergens.Where(a => a.Name == "乳").Select(a => a.Id).ToList();
        await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.New, labelIds: milkOnly));

        var check = Assert.Single(data.AddedChecks);
        Assert.Contains("職員が外した品目：卵", check.SourceNote);
    }

    [Fact]
    public async Task 読み取りの前に手で入力した未確定の商品は_読み取りのあとも残る()
    {
        var data = new InMemoryData();
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("のむヨーグルト", "125ml×6", "乳")));

        await service.AddManualAsync(menu.Id, new FlyerManualInput("てがきのせんべい", null, "10枚", 200, null, null, 10, SnackType.Salty, null), CancellationToken.None);
        await service.RunReadingAsync(menu.Id, [new ClaudeImage([1], "image/jpeg")], new NullProgress(), CancellationToken.None);
        var view = await service.GetLatestAsync(menu.Id);

        Assert.Contains(view.Value!.Session!.Items, i => i.IsManual && i.Read.Name == "てがきのせんべい");
        Assert.Contains(view.Value.Session.Items, i => i.Read.Name == "のむヨーグルト");
    }

    [Fact]
    public async Task SR_20_AI_が同じと判定した候補でも_確定の前に食い違いが分かる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Product("ぽりぽり スナック うすしお 6袋", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.Volume = "18g×6袋";
        data.Products.Add(existing);
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("ぽりぽり スナック うすしお", "18g×6袋", "卵・小麦")), verdict: ProductMatchVerdict.Same);

        var item = await ReadSingleAsync(service, menu);

        var plan = item.PlansByProduct[existing.Id];
        Assert.Contains(plan.Discrepancies, d => d.Allergen.Name == "卵" && d.IsAvoided);
        Assert.Equal(FlyerReferenceDecision.AddForDiscrepancy, plan.ReferenceDecision);
    }

    [Fact]
    public async Task 説明文に避ける品目の探す言葉があれば_含むとして参考の記録に入る()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("ことりのショコラ", "8個", "小麦", description: "ヘーゼルナッツの香ばしいクリーム")));

        var item = await ReadSingleAsync(service, menu);
        await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.New));

        Assert.Contains("ヘーゼルナッツ", item.DescriptionHitWords);
        var created = Assert.Single(data.Products);
        Assert.Equal(AllergenJudgementCategory.Blocked, AllergenJudge.Judge(created, data.Avoided.Select(a => a.Allergen), Today, RecheckDays).Category);
    }

    [Fact]
    public async Task SR_14_読んだ商品名の探す言葉は_取り込みで職員が名前を直しても使えないのまま()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var (service, _, menu) = Create(data, new FixedFlyerReader(Read("ヘーゼルナッツクッキー", "8個", "小麦")));

        var item = await ReadSingleAsync(service, menu);
        var result = await service.ConfirmItemAsync(Input(menu, item, FlyerItemChoice.New) with { Name = "さくさくクッキー" });

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Contains("ヘーゼルナッツ", item.DescriptionHitWords);
        var created = Assert.Single(data.Products);
        Assert.Equal("さくさくクッキー", created.Name);
        Assert.Equal(AllergenJudgementCategory.Blocked, AllergenJudge.Judge(created, data.Avoided.Select(a => a.Allergen), Today, RecheckDays).Category);
    }
}
