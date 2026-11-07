using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Tests.Fakes;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// チラシの取り込みのユースケース（D-101、09-flyer-import.md の 3.3・3.5〜3.8・5・10）。
/// 分類の求め直し・更新の操作・外す／戻す・取り込みを終える・取り込み済みの行の直し・確認済みとの食い違いの「参考」の記録・
/// 掲載品の写しの表記の原文を、メモリ上のリポジトリで確かめる。
/// </summary>
public class FlyerImportApplicationServiceTests
{
    private static FlyerReadProduct Read(
        string name,
        string? label = "小麦",
        string? volume = "10袋入",
        int? pieces = 10,
        string? description = null,
        IReadOnlyList<string>? unmapped = null,
        string? maker = TestMaker,
        int? price = 216,
        int? priceExcludingTax = 200,
        string? orderNo = "8101") => new(
        name, maker, volume, priceExcludingTax, price, orderNo, null, label, label is null, [], unmapped ?? [], description, null, pieces,
        pieces is null ? null : "10袋入りのため", SnackType.Sweet, [], []);

    private static FlyerReadProduct NonFood(string name) => Read(name) with { IsNonFood = true, NonFoodReason = "台所用の洗剤" };

    /// <summary>商品マスタの商品（メーカーは TestMaker、内容量 10袋入・入数10・しょっぱいもの）</summary>
    private static Product Existing(InMemoryData data, string name, params AllergenCheck[] checks)
    {
        var product = Product(name, checks);
        product.Volume = "10袋入";
        product.PiecesPerPack = 10;
        product.SnackType = SnackType.Salty;
        product.Price = 100;
        product.OrderNo = "0001";
        data.Products.Add(product);
        return product;
    }

    private static (FlyerImportApplicationService Service, Menu Menu, FixedFlyerReviewer Reviewer) Create(
        InMemoryData data, IFlyerReader reader, FixedFlyerReviewer? reviewer = null, bool loggedIn = true)
    {
        var menu = MenuTestData.NewMenu(20000, 50);
        data.Menus.Add(menu);
        reviewer ??= new FixedFlyerReviewer(new FlyerReview([], [], []));
        var service = new FlyerImportApplicationService(
            new FakeMenuRepository(data),
            new FakeProductRepository(data),
            new FakeSettingRepository(data),
            new FakeUnitOfWork(data),
            new FakeSessionStore(),
            new FakeJobs(),
            reader,
            reviewer,
            new FakeUserContext(loggedIn),
            new FixedTimeProvider(new DateTimeOffset(NoonUtc(Today))),
            new FlyerImportOptions());
        return (service, menu, reviewer);
    }

    private static async Task<FlyerSessionView> ReadAllAsync(FlyerImportApplicationService service, Menu menu)
    {
        var read = await service.RunReadingAsync(menu.Id, [new ClaudeImage([1, 2, 3], "image/jpeg")], new NullProgress(), CancellationToken.None);
        Assert.False(read.HasError, string.Join(",", read.ErrorMessages));
        return await ViewAsync(service, menu);
    }

    private static async Task<FlyerSessionView> ViewAsync(FlyerImportApplicationService service, Menu menu)
    {
        var view = await service.GetLatestAsync(menu.Id);
        Assert.False(view.HasError, string.Join(",", view.ErrorMessages));
        return view.Value!.Session!;
    }

    private static async Task<FlyerItemView> ReadSingleAsync(FlyerImportApplicationService service, Menu menu) =>
        Assert.Single((await ReadAllAsync(service, menu)).Items);

    private static async Task<FlyerFinishResult> FinishAsync(FlyerImportApplicationService service, Menu menu)
    {
        var result = await service.FinishAsync(menu.Id);
        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        return result.Value!;
    }

    private static FlyerItemView ItemOf(FlyerSessionView session, string name) => session.Items.Single(i => i.Read.Name == name);

    private static FlyerItemEditInput EditOf(Menu menu, FlyerItemView item) => new(
        menu.Id, item.Id, item.Read.Name, item.Read.Maker, item.Read.Volume, item.Read.PriceIncludingTax, item.Read.PriceExcludingTax,
        item.Read.OrderNo, item.Read.PiecesPerPackCandidate, item.Read.SnackType, item.Read.TargetAgeLabel);

    private static AllergenJudgementCategory JudgeOf(InMemoryData data, Product product) =>
        AllergenJudge.Judge(product, data.Avoided.Select(a => a.Allergen), Today, RecheckDays).Category;

    [Fact]
    public async Task 画像は読み取りと検証が終わったら破棄する()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("もぐもぐクッキー")));
        var images = new List<ClaudeImage> { new([1, 2, 3], "image/jpeg") };

        await service.RunReadingAsync(menu.Id, images, new NullProgress(), CancellationToken.None);

        Assert.Empty(images);
    }

    // ---- 一覧（09 の 6） ----

    [Fact]
    public async Task 一覧は再入力_取り込める_使えない_確定済みの順に並び_件数と取り込める件数と今週の商品に入らない件数を返す()
    {
        var data = new InMemoryData();
        Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("おおぶくろのあめ", pieces: null),
            Read("くまのビスケット"),
            Read("ことりのクッキー"),
            Read("ねこのせんべい", price: null, priceExcludingTax: null)));

        var session = await ReadAllAsync(service, menu);

        Assert.Equal(
            new[] { FlyerItemStatus.NeedsInput, FlyerItemStatus.Ready, FlyerItemStatus.Ready, FlyerItemStatus.Unusable },
            session.Items.Select(i => i.Status));
        Assert.Equal(new[] { "ねこのせんべい", "くまのビスケット", "ことりのクッキー", "おおぶくろのあめ" }, session.Items.Select(i => i.Read.Name));
        Assert.False(ItemOf(session, "くまのビスケット").IsNew);
        Assert.True(ItemOf(session, "ことりのクッキー").IsNew);
        Assert.Equal(new FlyerCategoryCounts(1, 2, 1, 0, 0), session.Counts);
        Assert.Equal(new FlyerFinishSummary(2, 1, 1), session.Finish);
        Assert.Equal(new FlyerNotListedCounts(2, 1, 1), session.NotListed);
        Assert.Equal(4, session.UnconfirmedCount);

        await FinishAsync(service, menu);
        var after = await ViewAsync(service, menu);

        Assert.Equal(
            new[] { FlyerItemStatus.NeedsInput, FlyerItemStatus.Unusable, FlyerItemStatus.Confirmed, FlyerItemStatus.Confirmed },
            after.Items.Select(i => i.Status));
        Assert.Equal(new FlyerCategoryCounts(1, 0, 1, 2, 0), after.Counts);
        Assert.Equal(new FlyerFinishSummary(0, 0, 0), after.Finish);
        Assert.Equal(2, after.UnconfirmedCount);
        Assert.False(ItemOf(after, "くまのビスケット").IsNew);
        Assert.True(ItemOf(after, "ことりのクッキー").IsNew);
    }

    [Fact]
    public async Task 取り込めるの行には_一致した商品とマスタでのアレルゲンの表示を返す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "くまのビスケット", Reference(Today.AddDays(-5), "wheat"));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ＣＯ・ＯＰ　くまのビスケット")));

        var item = await ReadSingleAsync(service, menu);

        // 記録が参考でも、避ける品目がなければ取り込みの分類は「取り込める」。アレルゲンの判定は 🟡 のまま（09 の 3.1）
        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        Assert.False(item.IsNew);
        Assert.Equal(existing.Id, item.MatchedProduct!.Id);
        Assert.Equal(AllergenJudgementCategory.Warning, item.MatchedProduct.Judgement.Category);
        Assert.Equal(0, item.OtherMatchCount);
    }

    // ---- 入数（FR-IMPORT-10・11） ----

    [Fact]
    public async Task FR_IMPORT_10_入数が分からない商品は一覧から外さず使えないにし_終えても取り込まない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("おおぶくろのあめ", volume: "120g", pieces: null)));

        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Unusable, item.Category);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.PiecesUnknown);
        var result = await FinishAsync(service, menu);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.NotListedUnusableCount);
        Assert.Empty(data.Products);
        Assert.Empty(data.AddedChecks);
        Assert.Equal(0, data.SaveCount);
    }

    [Fact]
    public async Task FR_IMPORT_10_入数の候補がなくても_一致した商品に入数があれば使えないにならない()
    {
        var data = new InMemoryData();
        Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", pieces: null)));

        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        Assert.Equal(10, item.PiecesPerPack);
    }

    [Fact]
    public async Task FR_IMPORT_11_入数を入れると取り込めるになり_注意を出して終えると取り込む()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("おおぶくろのあめ", volume: "12個", pieces: null)));
        var item = await ReadSingleAsync(service, menu);

        var entered = await service.EnterPiecesAsync(menu.Id, item.Id, 12);

        Assert.False(entered.HasError, string.Join(",", entered.ErrorMessages));
        Assert.Equal(FlyerItemCategory.Ready, entered.Value!.Category);
        Assert.Contains(entered.Value.Notes, n => n.Code == FlyerItemReasonCode.NotReviewed);
        var result = await FinishAsync(service, menu);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(12, Assert.Single(data.Products).PiecesPerPack);
    }

    [Fact]
    public async Task FR_IMPORT_11_行を直す操作で入数を入れても_注意を出して取り込む()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("おおぶくろのあめ", volume: "12個", pieces: null)));
        var item = await ReadSingleAsync(service, menu);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.PiecesUnknown);

        // 画面を通さずに、行を直す API で入数を入れる
        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PiecesPerPack = 12 });

        Assert.False(edited.HasError, string.Join(",", edited.ErrorMessages));
        Assert.Equal(FlyerItemCategory.Ready, edited.Value!.Category);
        Assert.True(edited.Value.IsPiecesEnteredByStaff);
        Assert.Contains(edited.Value.Notes, n => n.Code == FlyerItemReasonCode.NotReviewed);
        Assert.Equal(1, (await FinishAsync(service, menu)).CreatedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task FR_IMPORT_11_入れる入数は1以上の整数だけ(int? pieces)
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("おおぶくろのあめ", pieces: null)));
        var item = await ReadSingleAsync(service, menu);

        var entered = await service.EnterPiecesAsync(menu.Id, item.Id, pieces);

        Assert.True(entered.HasError);
    }

    // ---- 外した掲載品（FR-IMPORT-20・21） ----

    [Fact]
    public async Task FR_IMPORT_20_食品以外は外した掲載品に入り_終えても取り込まず_戻すと分類し直す()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー"), NonFood("きらきら洗剤")));

        var session = await ReadAllAsync(service, menu);

        var removed = Assert.Single(session.RemovedItems);
        Assert.Equal("きらきら洗剤", removed.Name);
        Assert.True(removed.IsNonFood);
        Assert.Contains("台所用の洗剤", removed.Reason);
        Assert.DoesNotContain(session.Items, i => i.Id == removed.Id);
        await FinishAsync(service, menu);
        Assert.Equal("ことりのクッキー", Assert.Single(data.Products).Name);

        var restored = await service.RestoreItemAsync(menu.Id, removed.Id);
        Assert.Equal(FlyerItemCategory.Ready, restored.Value!.Category);
        Assert.Empty((await ViewAsync(service, menu)).RemovedItems);
    }

    [Fact]
    public async Task FR_IMPORT_21_どの分類でも外せて_外した掲載品は取り込まない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);

        await service.RemoveItemAsync(menu.Id, item.Id);
        var result = await FinishAsync(service, menu);

        Assert.Equal("職員が外しました", Assert.Single((await ViewAsync(service, menu)).RemovedItems).Reason);
        Assert.Empty(result.ConfirmedItemIds);
        Assert.Empty(data.Products);
        Assert.Empty(menu.ListedProducts);
    }

    // ---- 二重の読み取り（FR-IMPORT-06） ----

    [Fact]
    public async Task FR_IMPORT_06_二重の読み取りは1件にまとめ_検証にはまとめる前の一覧を渡し_指摘と食い違いは注意になる()
    {
        var data = new InMemoryData();
        var review = new FlyerReview([new FlyerReviewNote(2, "内容量", "10袋入と読めます")], [], []);
        var (service, menu, reviewer) = Create(
            data,
            new FixedFlyerReader(Read("ことりのクッキー"), NonFood("きらきら洗剤"), Read("ことりのクッキー", price: 220, priceExcludingTax: 204)),
            new FixedFlyerReviewer(review));

        var session = await ReadAllAsync(service, menu);

        Assert.Equal(3, reviewer.Received!.Items.Count);
        var item = Assert.Single(session.Items);
        Assert.Equal(2, item.SourceCount);
        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        Assert.Empty(item.Reasons);
        Assert.Contains(item.Notes, r => r.Code == FlyerItemReasonCode.ReviewNote && r.Message == "10袋入と読めます");
        var conflict = Assert.Single(item.Notes, r => r.Key == $"{nameof(FlyerItemReasonCode.DuplicateConflict)}:{FlyerItemField.PriceIncludingTax}");
        Assert.Contains("216 を使います", conflict.Message);
        Assert.Single(session.RemovedItems);

        // 先に読んだ値で取り込む
        await FinishAsync(service, menu);
        Assert.Equal(216, Assert.Single(menu.ListedProducts).Price);
    }

    [Fact]
    public async Task FR_IMPORT_06_二重に読んだ表記の品目は和集合になり_写しの表記はつないだ原文になる()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー", label: "小麦"), Read("ことりのクッキー", label: "乳")));

        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);

        Assert.Equal(new[] { Allergen("wheat").Id, Allergen("milk").Id }.ToHashSet(), item.LabelAllergens.Select(a => a.Id).ToHashSet());
        Assert.Equal("小麦／乳", item.ListingLabelText);
        Assert.Equal("小麦／乳", Assert.Single(menu.ListedProducts).LabelText);
    }

    // ---- 取り込みを終える（09 の 5.1） ----

    [Fact]
    public async Task 取り込めるの商品は_新しい商品も既存の商品も手入力も入数を入れ直した商品も表記なしも_1回で全件取り込む()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(
            data,
            new FixedFlyerReader(
                Read("くまのビスケット"),
                Read("ことりのクッキー"),
                Read("おおぶくろのあめ", volume: "12個", pieces: null),
                Read("りすのクッキー", label: null)),
            new FixedFlyerReviewer(null));
        var session = await ReadAllAsync(service, menu);
        Assert.True(session.ReviewFailed);
        await service.EnterPiecesAsync(menu.Id, ItemOf(session, "おおぶくろのあめ").Id, 12);
        var manual = await service.AddManualAsync(
            menu.Id, new FlyerManualInput("てがきのせんべい", TestMaker, "10枚", 216, 200, "8102", 10, SnackType.Salty, "小麦"), CancellationToken.None);
        Assert.False(manual.HasError, string.Join(",", manual.ErrorMessages));
        session = await ViewAsync(service, menu);
        Assert.All(session.Items, i => Assert.Equal(FlyerItemStatus.Ready, i.Status));
        Assert.Equal(new FlyerFinishSummary(5, 4, 1), session.Finish);

        var result = await FinishAsync(service, menu);

        Assert.Equal(4, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(5, result.ConfirmedItemIds.Count);
        Assert.Equal(5, data.Products.Count);
        Assert.Equal(5, menu.ListedProducts.Count);
        Assert.Contains(menu.ListedProducts, l => l.ProductId == existing.Id);
        Assert.Equal(1, data.SaveCount);
        Assert.All((await ViewAsync(service, menu)).Items, i => Assert.Equal(FlyerItemStatus.Confirmed, i.Status));
    }

    [Fact]
    public async Task 既存の商品は終えると_価格と注文番号と載った週は上書きされ_おやつの分類とタグは上書きされない()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット") with { CharacterTags = ["くま"] }));
        await ReadSingleAsync(service, menu);

        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(216, existing.Price);
        Assert.Equal(200, existing.PriceExcludingTax);
        Assert.Equal("8101", existing.OrderNo);
        Assert.Equal(menu.WeekStart, existing.ListedWeekStart);
        Assert.Equal(SnackType.Salty, existing.SnackType);
        Assert.Empty(existing.Tags);
        var listed = Assert.Single(menu.ListedProducts);
        Assert.Equal(existing.Id, listed.ProductId);
        Assert.Equal("小麦", listed.LabelText);
        // 確認済みの「含む」に収まるので、記録は足さない
        Assert.Empty(data.AddedChecks);
        Assert.Equal(FlyerItemStatus.Confirmed, Assert.Single((await ViewAsync(service, menu)).Items).Status);
    }

    [Fact]
    public async Task 既存の商品は_過去の週の取り込みでは上書きしない()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.ListedWeekStart = MenuTestData.WeekStart.AddDays(7);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット")));
        await ReadSingleAsync(service, menu);

        await FinishAsync(service, menu);

        Assert.Equal(100, existing.Price);
        Assert.Equal("0001", existing.OrderNo);
        Assert.Equal(216, Assert.Single(menu.ListedProducts).Price);
    }

    [Fact]
    public async Task FR_IMPORT_12_内容量と入数がマスタと違えば注意で取り込め_終えると内容量と入数を上書きする()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.Volume = "8袋入";
        existing.PiecesPerPack = 8;
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット")));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        var note = Assert.Single(item.Notes, r => r.Code == FlyerItemReasonCode.MasterVolumeOrPiecesChanged);
        Assert.Contains("内容量が変わりました", note.Message);
        Assert.Contains("届いたらパッケージの原材料も見てください", note.Message);

        await FinishAsync(service, menu);

        Assert.Equal("10袋入", existing.Volume);
        Assert.Equal(10, existing.PiecesPerPack);
        // 内容量が変わっても同じ商品として確認を引き継ぐ（記録は足さない。判定も変えない）
        Assert.Empty(data.AddedChecks);
        Assert.Equal(AllergenJudgementCategory.Safe, JudgeOf(data, existing));
    }

    [Fact]
    public async Task FR_IMPORT_12_過去の週は内容量と入数を上書きせず_空のときだけ埋める()
    {
        var data = new InMemoryData();
        var differs = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        differs.Volume = "8袋入";
        differs.PiecesPerPack = 8;
        differs.ListedWeekStart = MenuTestData.WeekStart.AddDays(7);
        var blank = Existing(data, "ねこのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        blank.Volume = null;
        blank.ListedWeekStart = MenuTestData.WeekStart.AddDays(7);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット"), Read("ねこのビスケット", volume: "12袋入", pieces: 12)));
        var session = await ReadAllAsync(service, menu);

        Assert.Contains("過去の週の取り込みなので、商品一覧は上書きしません", Assert.Single(ItemOf(session, "くまのビスケット").Notes).Message);
        await FinishAsync(service, menu);

        Assert.Equal("8袋入", differs.Volume);
        Assert.Equal(8, differs.PiecesPerPack);
        Assert.Equal("12袋入", blank.Volume);
        Assert.Equal(10, blank.PiecesPerPack);
    }

    [Fact]
    public async Task 読めなかった内容量と注文番号では_既存の商品を上書きしない()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", volume: null, orderNo: null)));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        Assert.Equal(
            new[] { FlyerItemReasonCode.MissingVolume, FlyerItemReasonCode.MissingOrderNo },
            item.Notes.Select(n => n.Code));
        await FinishAsync(service, menu);

        Assert.Equal("10袋入", existing.Volume);
        Assert.Equal("0001", existing.OrderNo);
        Assert.Null(Assert.Single(menu.ListedProducts).OrderNo);
    }

    [Fact]
    public async Task 一致した商品に記録がないときは_終えるとチラシの品目の参考の記録を足す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "くまのビスケット");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", label: "小麦")));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.UpdatedCount);
        var check = Assert.Single(existing.AllergenChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.Equal(Allergen("wheat").Id, Assert.Single(check.Items).AllergenId);
    }

    [Fact]
    public async Task 使えない_再入力_外した掲載品は終えても取り込まれず_ほかの取り込めるだけが取り込まれる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("たまごボーロ", label: "卵"),
            Read("ねこのせんべい", price: null, priceExcludingTax: null),
            Read("ふしぎなクッキー", label: "小麦・ナゾ成分", unmapped: ["ナゾ成分"]),
            Read("りすのクッキー"),
            Read("ことりのクッキー")));
        var session = await ReadAllAsync(service, menu);
        await service.RemoveItemAsync(menu.Id, ItemOf(session, "りすのクッキー").Id);

        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.NotListedUnusableCount);
        Assert.Equal(2, result.NotListedNeedsInputCount);
        var created = Assert.Single(data.Products);
        Assert.Equal("ことりのクッキー", created.Name);
        Assert.Equal(created.Id, Assert.Single(menu.ListedProducts).ProductId);
        Assert.Equal([ItemOf(session, "ことりのクッキー").Id], result.ConfirmedItemIds);
        var after = await ViewAsync(service, menu);
        Assert.Equal(FlyerItemStatus.Unusable, ItemOf(after, "たまごボーロ").Status);
        Assert.Equal(FlyerItemStatus.NeedsInput, ItemOf(after, "ねこのせんべい").Status);
        Assert.Equal(FlyerItemStatus.NeedsInput, ItemOf(after, "ふしぎなクッキー").Status);
    }

    [Fact]
    public async Task 取り込めるが0件で食い違いもなければ_何も保存しない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("たまごボーロ", label: "卵")));
        await ReadSingleAsync(service, menu);

        var result = await FinishAsync(service, menu);

        Assert.Empty(result.ConfirmedItemIds);
        Assert.Equal(1, result.NotListedUnusableCount);
        Assert.Equal(0, data.SaveCount);
    }

    [Fact]
    public async Task ログインしていなければ_終えても何も保存しない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "卵・小麦"), Read("ことりのクッキー")), loggedIn: false);
        await ReadAllAsync(service, menu);

        var result = await service.FinishAsync(menu.Id);

        Assert.True(result.HasError);
        Assert.Single(existing.AllergenChecks);
        Assert.Single(data.Products);
        Assert.Empty(data.AddedChecks);
        Assert.Empty(menu.ListedProducts);
        Assert.Equal(0, data.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Claude_が税抜価格を1未満で返したら読めなかった扱いにし_取り込めるの商品はほかの商品と一緒に取り込まれる(int priceExcludingTax)
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.PriceExcludingTax = 90;
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("ことりのクッキー"),
            Read("ねこのクッキー", priceExcludingTax: priceExcludingTax),
            Read("くまのビスケット", priceExcludingTax: priceExcludingTax)));
        var session = await ReadAllAsync(service, menu);
        var cat = ItemOf(session, "ねこのクッキー");
        Assert.Equal(FlyerItemCategory.Ready, cat.Category);
        Assert.Null(cat.Read.PriceExcludingTax);
        Assert.DoesNotContain(cat.Notes, n => n.Code == FlyerItemReasonCode.TaxMismatch);

        var result = await FinishAsync(service, menu);

        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Null(data.Products.Single(p => p.Name == "ねこのクッキー").PriceExcludingTax);
        // 読めなかった税抜（null）では、登録済みの商品の値を消さない
        Assert.Equal(90, existing.PriceExcludingTax);
        Assert.Equal(3, menu.ListedProducts.Count);
    }

    [Fact]
    public async Task Claude_が空のタグや選べる語にない味のタグを返しても_新しい商品は取り込める()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("ことりのクッキー") with { CharacterTags = ["", "  ", " ことり "], FlavorTags = ["チョコ", "ふしぎな味", " "] }));
        await ReadSingleAsync(service, menu);

        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.CreatedCount);
        var created = Assert.Single(data.Products);
        Assert.Equal(["ことり"], created.Tags.Where(t => t.Kind == ProductTagKind.Character).Select(t => t.Value));
        Assert.DoesNotContain(created.Tags, t => t.Value == "ふしぎな味" || string.IsNullOrWhiteSpace(t.Value));
    }

    [Fact]
    public async Task 新しい商品は_Claude_の候補で登録し_チラシの品目の参考の記録を足す()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー") with { CharacterTags = ["ことり"], FlavorTags = [] }));
        await ReadSingleAsync(service, menu);

        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.CreatedCount);
        var created = Assert.Single(data.Products);
        Assert.Equal("ことりのクッキー", created.Name);
        Assert.Equal(TestMaker, created.Maker);
        Assert.Equal(SnackType.Sweet, created.SnackType);
        Assert.Equal(10, created.PiecesPerPack);
        Assert.Equal(216, created.Price);
        Assert.Contains(created.Tags, t => t.Value == "ことり");
        var check = Assert.Single(created.AllergenChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.All(check.Items, i => Assert.True(i.Contains));
    }

    [Fact]
    public async Task 新しい商品でおやつの分類がなければ再入力で_終えても登録しない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("うさぎのクッキー") with { SnackType = null }));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.MissingSnackType && r.Field == FlyerItemField.SnackType);
        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.NotListedNeedsInputCount);
        Assert.Empty(data.Products);

        // 分類を選ぶと取り込める
        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { SnackType = SnackType.Sweet });
        Assert.Equal(FlyerItemCategory.Ready, edited.Value!.Category);
        Assert.Equal(1, (await FinishAsync(service, menu)).CreatedCount);
    }

    // ---- 表記なし（09 の 5.3。D-97 の見比べはなくした） ----

    [Fact]
    public async Task 表記なしの新しい商品は_終えると品目が空の参考になり_含まないにも安全にもならず_写しの表記は表記なし()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー", label: null)));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        Assert.True(item.IsLabelAbsent);
        Assert.Equal("表記なし", item.ListingLabelText);

        await FinishAsync(service, menu);

        var created = Assert.Single(data.Products);
        var check = Assert.Single(created.AllergenChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.Empty(check.Items);
        Assert.DoesNotContain(check.Items, i => !i.Contains);
        Assert.StartsWith("表記なし", check.SourceNote);
        Assert.NotEqual(AllergenJudgementCategory.Safe, JudgeOf(data, created));
        Assert.Equal("表記なし", Assert.Single(menu.ListedProducts).LabelText);
    }

    [Fact]
    public async Task 表記なしの既存の商品は_記録で特定原材料を含めば再入力で終えても取り込まず_品目を入れれば取り込める()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat", "milk"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", label: null)));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);
        var reason = Assert.Single(item.Reasons);
        Assert.Equal(FlyerItemReasonCode.LabelAbsentButMasterContains, reason.Code);
        Assert.Equal(new[] { Allergen("wheat").Id, Allergen("milk").Id }, reason.Allergens.Select(a => a.Id));
        Assert.Empty((await FinishAsync(service, menu)).ConfirmedItemIds);
        Assert.Empty(menu.ListedProducts);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { LabelAllergenIds = [Allergen("wheat").Id, Allergen("milk").Id] });
        Assert.Equal(FlyerItemCategory.Ready, edited.Value!.Category);
        Assert.False(edited.Value.IsLabelAbsent);
        Assert.Equal("表記なし（品目を入れました：小麦・乳）", edited.Value.ListingLabelText);
        await FinishAsync(service, menu);

        var listed = Assert.Single(menu.ListedProducts);
        Assert.Equal(existing.Id, listed.ProductId);
        Assert.Equal("表記なし（品目を入れました：小麦・乳）", listed.LabelText);
    }

    // ---- 確認済みとの食い違い（SR-20、09 の 3.5） ----

    [Fact]
    public async Task SR_20_確認済みの商品でチラシに避ける品目が増えたら使えないになり_終えると参考の記録が足され含むになる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "卵・小麦"), Read("ことりのクッキー")));
        var session = await ReadAllAsync(service, menu);
        var item = ItemOf(session, "さくさくクッキー");

        Assert.Equal(FlyerItemCategory.Unusable, item.Category);
        Assert.Contains(item.Discrepancies, d => d.Allergen.Id == Allergen("egg").Id && d.IsAvoided);
        Assert.Contains(item.Notes, n => n.Code == FlyerItemReasonCode.MasterDiscrepancy);
        // 読み取りが終わった時点では保存しない
        Assert.Empty(data.AddedChecks);

        // 取り込みを終えると、使えない商品の分もまとめて足す
        var result = await FinishAsync(service, menu);

        var recorded = Assert.Single(result.RecordedProducts);
        Assert.Equal(existing.Id, recorded.ProductId);
        Assert.Equal(2, existing.AllergenChecks.Count);
        Assert.Equal(AllergenJudgementKind.Contains, AllergenJudge.Judge(existing, data.Avoided.Select(a => a.Allergen), Today, RecheckDays).Kind);
        Assert.DoesNotContain(menu.ListedProducts, l => l.ProductId == existing.Id);
        Assert.Contains(ItemOf(await ViewAsync(service, menu), "さくさくクッキー").RecordedDiscrepancyAllergens, a => a.Id == Allergen("egg").Id);
    }

    [Fact]
    public async Task SR_20_避ける品目でない品目が増えたら取り込めるのまま_終えると参考の記録が足され警告になる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳・小麦")));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        Assert.Contains(item.Discrepancies, d => d.Allergen.Id == Allergen("milk").Id && !d.IsAvoided);
        Assert.Equal(AllergenJudgementCategory.Safe, JudgeOf(data, existing));

        var result = await FinishAsync(service, menu);

        Assert.Single(result.RecordedProducts);
        var check = Assert.Single(data.AddedChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.All(check.Items, i => Assert.True(i.Contains));
        Assert.Equal(AllergenJudgementCategory.Warning, JudgeOf(data, existing));
        Assert.Equal(existing.Id, Assert.Single(menu.ListedProducts).ProductId);
    }

    [Fact]
    public async Task SR_20_取り込めるが0件でも_食い違いがあれば終えると参考の記録を足す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "卵・小麦")));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(FlyerItemCategory.Unusable, item.Category);

        var result = await FinishAsync(service, menu);

        Assert.Empty(result.ConfirmedItemIds);
        Assert.Equal(existing.Id, Assert.Single(result.RecordedProducts).ProductId);
        Assert.Equal(2, existing.AllergenChecks.Count);
        Assert.Equal(1, data.SaveCount);
        Assert.Empty(menu.ListedProducts);
    }

    [Fact]
    public async Task SR_20_終える前に職員が表記を直して食い違いがなくなれば_記録は足されない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳・小麦")));
        var item = await ReadSingleAsync(service, menu);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { LabelAllergenIds = [Allergen("wheat").Id] });
        Assert.Empty(edited.Value!.Discrepancies);
        var result = await FinishAsync(service, menu);

        Assert.Empty(result.RecordedProducts);
        Assert.Empty(data.AddedChecks);
        Assert.Single(existing.AllergenChecks);
        Assert.Equal("乳・小麦（品目を直しました：小麦）", Assert.Single(menu.ListedProducts).LabelText);
    }

    [Fact]
    public async Task SR_20_チラシの品目が確認済みの含むの中に収まっていれば_記録は足されない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat", "milk"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳")));
        var item = await ReadSingleAsync(service, menu);

        await FinishAsync(service, menu);

        Assert.Empty(item.Discrepancies);
        Assert.Empty(data.AddedChecks);
        Assert.Single(existing.AllergenChecks);
    }

    [Fact]
    public async Task SR_20_品目の分からない語が残る再入力の商品でも_対応づけできた品目で食い違いの記録を足す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("さくさくクッキー", label: "乳・ナゾ成分", unmapped: ["ナゾ成分"]),
            Read("ことりのクッキー")));
        var session = await ReadAllAsync(service, menu);
        Assert.Equal(FlyerItemCategory.NeedsInput, ItemOf(session, "さくさくクッキー").Category);

        await FinishAsync(service, menu);

        var check = Assert.Single(existing.AllergenChecks, c => c.Status == AllergenCheckStatus.Reference);
        Assert.Contains(check.Items, i => i.AllergenId == Allergen("milk").Id && i.Contains);
        Assert.DoesNotContain(menu.ListedProducts, l => l.ProductId == existing.Id);
    }

    [Fact]
    public async Task SR_20_外した掲載品も_確認済みとの食い違いがあれば_終えると参考の記録が足される()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳・小麦"), Read("ことりのクッキー")));
        var session = await ReadAllAsync(service, menu);
        await service.RemoveItemAsync(menu.Id, ItemOf(session, "さくさくクッキー").Id);

        var removed = Assert.Single((await ViewAsync(service, menu)).RemovedItems);
        Assert.Equal([Allergen("milk").Name], removed.DiscrepancyAllergens);

        var result = await FinishAsync(service, menu);

        Assert.Equal(existing.Id, Assert.Single(result.RecordedProducts).ProductId);
        var check = Assert.Single(existing.AllergenChecks, c => c.Status == AllergenCheckStatus.Reference);
        Assert.Contains(check.Items, i => i.AllergenId == Allergen("milk").Id && i.Contains);
        Assert.Equal(AllergenJudgementCategory.Warning, JudgeOf(data, existing));
        // 外した掲載品は今週の商品に入れない（FR-IMPORT-22）
        Assert.DoesNotContain(menu.ListedProducts, l => l.ProductId == existing.Id);
        Assert.Empty(Assert.Single((await ViewAsync(service, menu)).RemovedItems).DiscrepancyAllergens);
    }

    [Fact]
    public async Task SR_20_表記を直してから外した掲載品は_食い違いがなければ記録を足さない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳・小麦"), Read("ことりのクッキー")));
        var session = await ReadAllAsync(service, menu);
        var item = ItemOf(session, "さくさくクッキー");
        await service.UpdateItemAsync(EditOf(menu, item) with { LabelAllergenIds = [Allergen("wheat").Id] });
        await service.RemoveItemAsync(menu.Id, item.Id);

        await FinishAsync(service, menu);

        Assert.Single(existing.AllergenChecks);
    }

    [Fact]
    public async Task SR_20_同じ食い違いの記録は_2回終えても足さない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "卵・小麦"), Read("ことりのクッキー")));
        await ReadAllAsync(service, menu);

        await FinishAsync(service, menu);
        await service.AddManualAsync(menu.Id, new FlyerManualInput("りすのクッキー", TestMaker, "10袋入", 216, 200, "8103", 10, SnackType.Sweet, "小麦"), CancellationToken.None);
        var second = await FinishAsync(service, menu);

        Assert.Single(second.ConfirmedItemIds);
        Assert.Empty(second.RecordedProducts);
        Assert.Equal(2, existing.AllergenChecks.Count);
    }

    // ---- 取り込み済みの行を直す（決めたこと B） ----

    [Fact]
    public async Task 取り込み済みの行を直して避ける品目を足すと_直した時点で写しから外れ_登録した商品は使わないになり_終えても取り込まない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        var created = Assert.Single(data.Products);
        Assert.Single(menu.ListedProducts);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { LabelAllergenIds = [Allergen("wheat").Id, Allergen("egg").Id] });

        Assert.False(edited.HasError, string.Join(",", edited.ErrorMessages));
        Assert.Equal(FlyerItemStatus.Unusable, edited.Value!.Status);
        Assert.Null(edited.Value.Confirmation);
        Assert.Empty(menu.ListedProducts);
        Assert.True(created.IsDisabled);

        await FinishAsync(service, menu);
        Assert.Empty(menu.ListedProducts);
        Assert.True(created.IsDisabled);
    }

    [Fact]
    public async Task 取り込み済みの行を直して取り込めるのままなら_終えると同じ商品で取り込み直す()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        var created = Assert.Single(data.Products);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PriceIncludingTax = 238, PriceExcludingTax = 220 });

        Assert.Equal(FlyerItemStatus.Ready, edited.Value!.Status);
        Assert.Empty(menu.ListedProducts);
        var result = await FinishAsync(service, menu);
        Assert.Equal(1, result.CreatedCount);
        Assert.Same(created, Assert.Single(data.Products));
        Assert.False(created.IsDisabled);
        Assert.Equal(238, created.Price);
        var listed = Assert.Single(menu.ListedProducts);
        Assert.Equal(created.Id, listed.ProductId);
        Assert.Equal(238, listed.Price);
    }

    [Fact]
    public async Task 取り込み済みの行に値の変わらない直しを送っても_取り込みは取り消さない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        var saves = data.SaveCount;

        var edited = await service.UpdateItemAsync(EditOf(menu, item));

        Assert.Equal(FlyerItemStatus.Confirmed, edited.Value!.Status);
        Assert.Single(menu.ListedProducts);
        Assert.Equal(saves, data.SaveCount);
    }

    [Fact]
    public async Task 取り込み済みの既存の商品の行を直して再入力になると_写しから外れ_既存の商品は使わないにしない()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PriceIncludingTax = null, PriceExcludingTax = null });

        Assert.Equal(FlyerItemStatus.NeedsInput, edited.Value!.Status);
        Assert.Empty(menu.ListedProducts);
        Assert.False(existing.IsDisabled);
        Assert.Empty((await FinishAsync(service, menu)).ConfirmedItemIds);
        Assert.Empty(menu.ListedProducts);
    }

    [Fact]
    public async Task この行で登録して使わないに戻した商品が_パッケージの確認で避ける品目を含めば_その行は使えないになる()
    {
        // 決めたこと H
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        var created = Assert.Single(data.Products);
        // 届いたパッケージで卵を含むと確かめた
        created.AllergenChecks.Add(ConfirmedAll(Today, ["wheat", "egg"]));

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PriceIncludingTax = 238, PriceExcludingTax = 220 });

        Assert.Equal(FlyerItemStatus.Unusable, edited.Value!.Status);
        Assert.Contains(edited.Value.Reasons, r => r.Code == FlyerItemReasonCode.AvoidedInMaster && r.Message.Contains("卵", StringComparison.Ordinal));
        Assert.True(created.IsDisabled);
        await FinishAsync(service, menu);
        Assert.Empty(menu.ListedProducts);
        Assert.True(created.IsDisabled);
    }

    [Fact]
    public async Task 取り込み済みを外すと取り込みを取り消し_戻して終えると同じ商品を使い直す()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        var created = Assert.Single(data.Products);
        Assert.Single(menu.ListedProducts);

        var removed = await service.RemoveItemAsync(menu.Id, item.Id);

        Assert.False(removed.HasError, string.Join(",", removed.ErrorMessages));
        Assert.Empty(menu.ListedProducts);
        Assert.True(created.IsDisabled);

        var restored = await service.RestoreItemAsync(menu.Id, item.Id);
        Assert.Equal(FlyerItemCategory.Ready, restored.Value!.Category);
        await FinishAsync(service, menu);

        Assert.Same(created, Assert.Single(data.Products));
        Assert.False(created.IsDisabled);
        Assert.Equal(created.Id, Assert.Single(menu.ListedProducts).ProductId);
    }

    [Fact]
    public async Task 新しく登録した商品をほかの行も使っているときは_取り込みを取り消しても使わないにしない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        var created = Assert.Single(data.Products);
        var manual = await service.AddManualAsync(
            menu.Id, new FlyerManualInput("ことりのクッキー", TestMaker, "10袋入", 216, 200, "8101", 10, SnackType.Sweet, "小麦"), CancellationToken.None);
        Assert.False(manual.HasError, string.Join(",", manual.ErrorMessages));
        Assert.Equal(1, (await FinishAsync(service, menu)).UpdatedCount);

        var removed = await service.RemoveItemAsync(menu.Id, item.Id);

        Assert.False(removed.HasError, string.Join(",", removed.ErrorMessages));
        Assert.False(created.IsDisabled);
        Assert.Equal(created.Id, Assert.Single(menu.ListedProducts).ProductId);
    }

    [Fact]
    public async Task 取り込み直しでは_最初に終えたときに前の写しを空にし_取り込めるが0件なら空にしない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var old = Existing(data, "むかしのせんべい", ConfirmedAll(Today.AddDays(-5), []));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("たまごボーロ", label: "卵"), Read("ことりのクッキー")));
        menu.ListedProducts.Add(new ListedProduct(old.Id, 100, "0001"));
        var session = await ReadAllAsync(service, menu);
        await service.RemoveItemAsync(menu.Id, ItemOf(session, "ことりのクッキー").Id);

        await FinishAsync(service, menu);
        Assert.Equal(old.Id, Assert.Single(menu.ListedProducts).ProductId);

        await service.RestoreItemAsync(menu.Id, ItemOf(session, "ことりのクッキー").Id);
        await FinishAsync(service, menu);

        Assert.DoesNotContain(menu.ListedProducts, l => l.ProductId == old.Id);
        Assert.Single(menu.ListedProducts);
    }

    // ---- 同じ取り込みの中の、名前＋メーカーが同じ行 ----

    [Fact]
    public async Task 手入力の行と読み取った行が同じ名前とメーカーなら_終えると商品は1件だけになり_品目は参考の記録の和集合になる()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのショコラ", label: "小麦")));
        var read = await ReadSingleAsync(service, menu);
        // 読み取りの検証が「読み落とした商品」として挙げたものを、職員が手で足した（実は読めていた商品）
        var manual = await service.AddManualAsync(
            menu.Id, new FlyerManualInput("ことりのショコラ", TestMaker, "12個入", 324, 300, "8109", 12, SnackType.Sweet, "小麦・乳"), CancellationToken.None);
        Assert.False(manual.HasError, string.Join(",", manual.ErrorMessages));
        var session = await ViewAsync(service, menu);
        Assert.All(session.Items, i => Assert.Equal(FlyerItemCategory.Ready, i.Category));
        Assert.Equal((2, 1, 0), (session.Finish.ReadyCount, session.Finish.NewCount, session.Finish.ExistingCount));

        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(2, result.ConfirmedItemIds.Count);
        var product = Assert.Single(data.Products);
        // 商品の値は先の行（読み取った行）の値
        Assert.Equal("10袋入", product.Volume);
        Assert.Equal(216, product.Price);
        Assert.Equal(10, product.PiecesPerPack);
        // チラシの品目は、どちらの行の品目も「含む」になる（確認する側に倒す）
        var contained = AllergenCheckHistory.From(product.AllergenChecks).ContainedAllergenIds;
        Assert.Contains(Allergen("wheat").Id, contained);
        Assert.Contains(Allergen("milk").Id, contained);
        Assert.All(product.AllergenChecks, c => Assert.Equal(AllergenCheckStatus.Reference, c.Status));
        var listed = Assert.Single(menu.ListedProducts);
        Assert.Equal(product.Id, listed.ProductId);
        Assert.Equal(216, listed.Price);
        Assert.Equal("小麦／小麦・乳", listed.LabelText);

        // 取り込んだ2行をどちらも取り消すと、写しから外れ、登録した商品は「使わない」になる
        await service.RemoveItemAsync(menu.Id, read.Id);
        Assert.False(product.IsDisabled);
        Assert.Single(menu.ListedProducts);
        await service.RemoveItemAsync(menu.Id, manual.Value!);
        Assert.True(product.IsDisabled);
        Assert.Empty(menu.ListedProducts);
    }

    [Fact]
    public async Task メーカーを直して同じ名前とメーカーになった行も_終えると1件の商品にまとめる()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("ことりのショコラ", label: "小麦"),
            Read("ことりのショコラ", label: "乳", maker: null, orderNo: "8110")));
        var session = await ReadAllAsync(service, menu);
        var missingMaker = Assert.Single(session.Items, i => i.Category == FlyerItemCategory.NeedsInput);

        var edited = await service.UpdateItemAsync(EditOf(menu, missingMaker) with { Maker = TestMaker });
        Assert.Equal(FlyerItemCategory.Ready, edited.Value!.Category);
        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.CreatedCount);
        var product = Assert.Single(data.Products);
        Assert.Equal("8101", product.OrderNo);
        var contained = AllergenCheckHistory.From(product.AllergenChecks).ContainedAllergenIds;
        Assert.Contains(Allergen("wheat").Id, contained);
        Assert.Contains(Allergen("milk").Id, contained);
        Assert.Equal(product.Id, Assert.Single(menu.ListedProducts).ProductId);
    }

    [Fact]
    public async Task 同じ商品の行が避ける品目で使えないなら_取り込んだ商品の参考の記録にその品目が入り_献立では入れられない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのショコラ", label: "小麦")));
        await ReadSingleAsync(service, menu);
        var manual = await service.AddManualAsync(
            menu.Id, new FlyerManualInput("ことりのショコラ", TestMaker, "10袋入", 216, 200, "8101", 10, SnackType.Sweet, "小麦・卵"), CancellationToken.None);
        var session = await ViewAsync(service, menu);
        Assert.Equal(FlyerItemCategory.Unusable, session.Items.Single(i => i.Id == manual.Value).Category);

        var result = await FinishAsync(service, menu);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.NotListedUnusableCount);
        var product = Assert.Single(data.Products);
        Assert.Contains(Allergen("egg").Id, AllergenCheckHistory.From(product.AllergenChecks).ContainedAllergenIds);
        Assert.Equal(AllergenJudgementCategory.Blocked, JudgeOf(data, product));
        Assert.Contains(product.AllergenChecks, c => c.SourceNote.Contains("今週の商品に入れなかった行", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 取り込み済みの商品と同じ商品の行が使えないなら_次に終えたときに参考の記録が足される()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "ことりのショコラ", Reference(Today.AddDays(-30), "wheat"));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのショコラ", label: "小麦"), Read("ねこのクッキー")));
        await ReadAllAsync(service, menu);
        await FinishAsync(service, menu);
        Assert.Equal(AllergenJudgementCategory.Warning, JudgeOf(data, existing));

        await service.AddManualAsync(
            menu.Id, new FlyerManualInput("ことりのショコラ", TestMaker, "10袋入", 216, 200, "8101", 10, SnackType.Sweet, "小麦・卵"), CancellationToken.None);
        await FinishAsync(service, menu);

        Assert.Equal(AllergenJudgementCategory.Blocked, JudgeOf(data, existing));
    }

    // ---- 取り込みを取り消したときの、上書きした値 ----

    [Fact]
    public async Task 取り込みを取り消すと_登録済みの商品に上書きした価格と内容量と入数と載った週を前の値に戻す()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.Volume = "8袋入";
        existing.PiecesPerPack = 8;
        existing.PriceExcludingTax = 93;
        existing.ListedWeekStart = MenuTestData.WeekStart.AddDays(-14);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", pieces: 12, orderNo: "8120")));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        Assert.Equal((216, 200, "8120", "10袋入", 12), (existing.Price, existing.PriceExcludingTax, existing.OrderNo, existing.Volume, existing.PiecesPerPack));
        Assert.Equal(MenuTestData.WeekStart, existing.ListedWeekStart);

        var removed = await service.RemoveItemAsync(menu.Id, item.Id);

        Assert.False(removed.HasError, string.Join(",", removed.ErrorMessages));
        Assert.Equal((100, 93, "0001", "8袋入", 8), (existing.Price, existing.PriceExcludingTax, existing.OrderNo, existing.Volume, existing.PiecesPerPack));
        Assert.Equal(MenuTestData.WeekStart.AddDays(-14), existing.ListedWeekStart);
        Assert.False(existing.IsDisabled);
        Assert.Empty(menu.ListedProducts);

        // 戻して終えると、もう一度チラシの値で上書きする
        await service.RestoreItemAsync(menu.Id, item.Id);
        await FinishAsync(service, menu);
        Assert.Equal(12, existing.PiecesPerPack);
    }

    [Fact]
    public async Task 取り込みのあとに商品一覧で直した値は_取り込みを取り消しても戻さない()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.PiecesPerPack = 8;
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", pieces: 12)));
        var item = await ReadSingleAsync(service, menu);
        await FinishAsync(service, menu);
        existing.PiecesPerPack = 6;

        await service.RemoveItemAsync(menu.Id, item.Id);

        Assert.Equal(6, existing.PiecesPerPack);
        // ほかの項目（まだ上書きした値のまま）は戻す
        Assert.Equal(100, existing.Price);
    }

    // ---- 照合・直し・語の選択 ----

    [Fact]
    public async Task FR_IMPORT_03_メーカーがない商品は照合せず再入力で_メーカーを入れると照合し直す()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", maker: null)));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);
        Assert.Null(item.MatchedProduct);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.MissingMaker);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { Maker = TestMaker });

        Assert.Equal(FlyerItemCategory.Ready, edited.Value!.Category);
        Assert.False(edited.Value.IsNew);
        Assert.Equal(existing.Id, edited.Value.MatchedProduct!.Id);
        Assert.Contains(FlyerItemField.Maker, edited.Value.EditedFields);
        Assert.Null(edited.Value.OriginalRead.Maker);
    }

    [Fact]
    public async Task 直した欄の読み取りの検証の指摘は消え_ほかの欄の指摘は注意として残る()
    {
        var data = new InMemoryData();
        var review = new FlyerReview([new FlyerReviewNote(0, "価格", "税込228円と読めます"), new FlyerReviewNote(0, "注文番号", "8110と読めます")], [], []);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")), new FixedFlyerReviewer(review));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(2, item.Notes.Count(r => r.Code == FlyerItemReasonCode.ReviewNote));
        Assert.Equal(FlyerItemCategory.Ready, item.Category);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PriceIncludingTax = 228, PriceExcludingTax = 211 });

        var remaining = Assert.Single(edited.Value!.Notes, r => r.Code == FlyerItemReasonCode.ReviewNote);
        Assert.Equal("8110と読めます", remaining.Message);
        Assert.Equal(FlyerItemCategory.Ready, edited.Value.Category);
        Assert.Equal(216, edited.Value.OriginalRead.PriceIncludingTax);
    }

    [Fact]
    public async Task 品目の分からない語に品目を選ぶと分類し直し_避ける品目を選べば使えないになる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ふしぎなクッキー", label: "小麦・ナゾ成分", unmapped: ["ナゾ成分"])));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);

        var none = await service.ResolveTermAsync(menu.Id, item.Id, "ナゾ成分", []);
        Assert.Equal(FlyerItemCategory.Ready, none.Value!.Category);
        Assert.Contains("ナゾ成分", none.Value.UnmappedTerms);

        var egg = await service.ResolveTermAsync(menu.Id, item.Id, "ナゾ成分", [Allergen("egg").Id]);
        Assert.Equal(FlyerItemCategory.Unusable, egg.Value!.Category);
        Assert.Empty((await FinishAsync(service, menu)).ConfirmedItemIds);
        Assert.Empty(data.Products);
    }

    [Fact]
    public async Task SR_14_読んだ商品名の探す言葉は_職員が名前を直しても使えないのまま()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ヘーゼルナッツクッキー")));
        var item = await ReadSingleAsync(service, menu);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { Name = "さくさくクッキー" });

        Assert.Equal(FlyerItemCategory.Unusable, edited.Value!.Category);
        Assert.Contains(edited.Value.Reasons, r => r.Code == FlyerItemReasonCode.AvoidedInName);
        Assert.Empty((await FinishAsync(service, menu)).ConfirmedItemIds);
    }

    [Fact]
    public async Task 説明文に避ける品目の探す言葉があれば使えない()
    {
        var data = new InMemoryData();
        data.Avoid("hazelnut");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのショコラ", description: "ヘーゼルナッツの香ばしいクリーム")));

        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Unusable, item.Category);
        Assert.Contains("ヘーゼルナッツ", item.DescriptionHitWords);
    }

    [Fact]
    public async Task SR_19_読み取りの検証は表記の品目を足すだけで_減らさず_注意になり_見つけた表記を写しに書き添える()
    {
        var data = new InMemoryData();
        var review = new FlyerReview([], [], [new FlyerMissedAllergenLabel(0, "乳", "説明文の末尾に（乳）があります")]);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("のむヨーグルト", label: "小麦")), new FixedFlyerReviewer(review));

        var item = await ReadSingleAsync(service, menu);

        Assert.Contains(item.LabelAllergens, a => a.Name == "乳");
        Assert.Contains(item.LabelAllergens, a => a.Name == "小麦");
        Assert.Equal(FlyerItemCategory.Ready, item.Category);
        Assert.Contains(item.Notes, r => r.Code == FlyerItemReasonCode.ReviewNote && r.IsAllergenRelated);
        Assert.Equal("小麦／乳", item.ListingLabelText);

        await FinishAsync(service, menu);
        var check = Assert.Single(Assert.Single(data.Products).AllergenChecks);
        Assert.Equal(new[] { Allergen("wheat").Id, Allergen("milk").Id }.ToHashSet(), check.Items.Select(i => i.AllergenId).ToHashSet());
        Assert.Equal("小麦／乳", Assert.Single(menu.ListedProducts).LabelText);
    }

    [Fact]
    public async Task 職員が外した表記の品目は_参考の記録に残り_写しの表記に直したことを書き添える()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("たまごボーロ", label: "卵・乳")));
        var item = await ReadSingleAsync(service, menu);

        await service.UpdateItemAsync(EditOf(menu, item) with { LabelAllergenIds = [Allergen("milk").Id] });
        await FinishAsync(service, menu);

        var check = Assert.Single(data.AddedChecks);
        Assert.Contains("職員が外した品目：卵", check.SourceNote);
        Assert.Equal(Allergen("milk").Id, Assert.Single(check.Items).AllergenId);
        Assert.Equal("卵・乳（品目を直しました：乳）", Assert.Single(menu.ListedProducts).LabelText);
    }

    [Fact]
    public async Task 手入力の商品は_入れた表記の原文を写しに入れ_表記がなければ表記なし()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader());
        await service.AddManualAsync(menu.Id, new FlyerManualInput("てがきのせんべい", TestMaker, "10枚", 216, 200, "8102", 10, SnackType.Salty, "小麦・大豆"), CancellationToken.None);
        await service.AddManualAsync(menu.Id, new FlyerManualInput("てがきのあめ", TestMaker, "10個", 108, 100, "8103", 10, SnackType.Sweet, null), CancellationToken.None);
        var session = await ViewAsync(service, menu);
        Assert.All(session.Items, i => Assert.Contains(i.Notes, n => n.Code == FlyerItemReasonCode.NotReviewed));

        await FinishAsync(service, menu);

        var labels = menu.ListedProducts.ToDictionary(l => data.Products.Single(p => p.Id == l.ProductId).Name, l => l.LabelText);
        Assert.Equal("小麦・大豆", labels["てがきのせんべい"]);
        Assert.Equal("表記なし", labels["てがきのあめ"]);
    }

    [Fact]
    public async Task 読み取りの前に手で入力した未確定の商品は_読み取りのあとも残る()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("のむヨーグルト", label: "乳")));

        await service.AddManualAsync(menu.Id, new FlyerManualInput("てがきのせんべい", TestMaker, "10枚", 216, 200, "8102", 10, SnackType.Salty, null), CancellationToken.None);
        var session = await ReadAllAsync(service, menu);

        Assert.Contains(session.Items, i => i.IsManual && i.Read.Name == "てがきのせんべい");
        Assert.Contains(session.Items, i => i.Read.Name == "のむヨーグルト");
    }

    [Fact]
    public async Task 略記の麦とカは品目に当て_Claude_の品目は消さない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのビスケット", label: "乳・麦・カ") with { AllergenCodes = ["egg"] }));

        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(
            new[] { Allergen("wheat").Id, Allergen("milk").Id, Allergen("egg").Id, Allergen("cashew").Id }.ToHashSet(),
            item.LabelAllergens.Select(a => a.Id).ToHashSet());
        Assert.Contains(item.AbbreviationHits, h => h.Term == "麦" && h.Allergen.Id == Allergen("wheat").Id);
        Assert.Contains(item.AbbreviationHits, h => h.Term == "カ" && h.Allergen.Id == Allergen("cashew").Id);
        Assert.Empty(item.UnmappedTerms);
    }
}
