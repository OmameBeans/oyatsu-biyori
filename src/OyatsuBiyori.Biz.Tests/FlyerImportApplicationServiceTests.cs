using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Tests.Fakes;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// チラシの取り込みのユースケース（D-100、09-flyer-import.md の 3.3・3.5〜3.7・5・10）。
/// 分類の求め直し・更新の操作・外す／戻す・確定（1件・まとめて）・確認済みとの食い違いの「参考」の記録を、メモリ上のリポジトリで確かめる。
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
        int? priceExcludingTax = 200) => new(
        name, maker, volume, priceExcludingTax, price, "8101", null, label, label is null, [], unmapped ?? [], description, null, pieces,
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

    private static (FlyerImportApplicationService Service, Menu Menu, FixedFlyerReviewer Reviewer) Create(InMemoryData data, IFlyerReader reader, FixedFlyerReviewer? reviewer = null)
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
            new FakeUserContext(),
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

    private static async Task<FlyerSessionView> ViewAsync(FlyerImportApplicationService service, Menu menu, IReadOnlyList<string>? editing = null)
    {
        var view = await service.GetLatestAsync(menu.Id, editing);
        Assert.False(view.HasError, string.Join(",", view.ErrorMessages));
        return view.Value!.Session!;
    }

    private static async Task<FlyerItemView> ReadSingleAsync(FlyerImportApplicationService service, Menu menu) =>
        Assert.Single((await ReadAllAsync(service, menu)).Items);

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

    [Fact]
    public async Task 一覧は再入力_要確認_使える_使えないの順に並び_件数と今週の商品に入らない件数を返す()
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
            new[] { FlyerItemStatus.NeedsInput, FlyerItemStatus.New, FlyerItemStatus.Usable, FlyerItemStatus.Unusable },
            session.Items.Select(i => i.Status));
        Assert.Equal(new FlyerCategoryCounts(1, 1, 1, 1, 0, 0), session.Counts);
        Assert.Equal(new FlyerNotListedCounts(4, 1, 1, 2), session.NotListed);
        Assert.Equal(1, session.BulkUsable.Count);
        Assert.Equal(1, session.BulkNew.Count);
    }

    [Fact]
    public async Task 使えるの行には_一致した商品とマスタでのアレルゲンの表示を返す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "くまのビスケット", Reference(Today.AddDays(-5), "wheat"));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ＣＯ・ＯＰ　くまのビスケット")));

        var item = await ReadSingleAsync(service, menu);

        // 記録が参考でも、避ける品目がなければ取り込みの分類は「使える」。アレルゲンの判定は 🟡 のまま（09 の 3.1）
        Assert.Equal(FlyerItemCategory.Usable, item.Category);
        Assert.Equal(existing.Id, item.MatchedProduct!.Id);
        Assert.Equal(AllergenJudgementCategory.Warning, item.MatchedProduct.Judgement.Category);
        Assert.Equal(0, item.OtherMatchCount);
    }

    [Fact]
    public async Task FR_IMPORT_10_入数が分からない商品は一覧から外さず使えないにし_記録も作らない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("おおぶくろのあめ", volume: "120g", pieces: null)));

        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Unusable, item.Category);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.PiecesUnknown);
        var confirm = await service.ConfirmItemAsync(menu.Id, item.Id);
        Assert.True(confirm.HasError);
        Assert.Empty(data.Products);
        Assert.Empty(data.AddedChecks);
    }

    [Fact]
    public async Task FR_IMPORT_10_入数の候補がなくても_一致した商品に入数があれば使えないにならない()
    {
        var data = new InMemoryData();
        Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", pieces: null)));

        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Usable, item.Category);
        Assert.Equal(10, item.PiecesPerPack);
    }

    [Fact]
    public async Task FR_IMPORT_11_入数を入れると分類し直され_まとめに入らず1件ずつなら確定できる()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("おおぶくろのあめ", volume: "12個", pieces: null)));
        var item = await ReadSingleAsync(service, menu);

        var entered = await service.EnterPiecesAsync(menu.Id, item.Id, 12);

        Assert.False(entered.HasError, string.Join(",", entered.ErrorMessages));
        Assert.Equal(FlyerItemCategory.New, entered.Value!.Category);
        Assert.False(entered.Value.CanBulk);
        Assert.Contains(FlyerBulkExclusion.PiecesEnteredByStaff, entered.Value.BulkExclusions);
        var bulk = await service.ConfirmAllNewAsync(menu.Id, [item.Id]);
        Assert.Equal(0, bulk.Value!.ConfirmedCount);
        Assert.Equal(1, bulk.Value.SkippedCount);
        var single = await service.ConfirmItemAsync(menu.Id, item.Id);
        Assert.False(single.HasError, string.Join(",", single.ErrorMessages));
        Assert.Equal(12, Assert.Single(data.Products).PiecesPerPack);
    }

    [Fact]
    public async Task FR_IMPORT_11_行を直す操作で入数を入れても_使えないから戻した商品はまとめに入らない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("おおぶくろのあめ", volume: "12個", pieces: null)));
        var item = await ReadSingleAsync(service, menu);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.PiecesUnknown);

        // 画面を通さずに、行を直す API で入数を入れる
        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PiecesPerPack = 12 });

        Assert.False(edited.HasError, string.Join(",", edited.ErrorMessages));
        Assert.DoesNotContain(edited.Value!.Reasons, r => r.Code == FlyerItemReasonCode.PiecesUnknown);
        Assert.False(edited.Value.CanBulk);
        Assert.Contains(FlyerBulkExclusion.PiecesEnteredByStaff, edited.Value.BulkExclusions);
        var bulk = await service.ConfirmAllNewAsync(menu.Id, [item.Id]);
        Assert.Equal(0, bulk.Value!.ConfirmedCount);
        Assert.Empty(data.Products);
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

    [Fact]
    public async Task FR_IMPORT_20_食品以外は外した掲載品に入り_確定できず_戻すと分類し直す()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー"), NonFood("きらきら洗剤")));

        var session = await ReadAllAsync(service, menu);

        var removed = Assert.Single(session.RemovedItems);
        Assert.Equal("きらきら洗剤", removed.Name);
        Assert.True(removed.IsNonFood);
        Assert.Contains("台所用の洗剤", removed.Reason);
        Assert.DoesNotContain(session.Items, i => i.Id == removed.Id);
        Assert.True((await service.ConfirmItemAsync(menu.Id, removed.Id)).HasError);

        var restored = await service.RestoreItemAsync(menu.Id, removed.Id);
        Assert.Equal(FlyerItemCategory.New, restored.Value!.Category);
        Assert.Empty((await ViewAsync(service, menu)).RemovedItems);
    }

    [Fact]
    public async Task FR_IMPORT_21_どの分類でも外せて_外した掲載品は確定できない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);

        await service.RemoveItemAsync(menu.Id, item.Id);
        var bulk = await service.ConfirmAllNewAsync(menu.Id, [item.Id]);

        Assert.Equal("職員が外しました", Assert.Single((await ViewAsync(service, menu)).RemovedItems).Reason);
        Assert.Equal(0, bulk.Value!.ConfirmedCount);
        Assert.Empty(data.Products);
        Assert.Empty(menu.ListedProducts);
    }

    [Fact]
    public async Task FR_IMPORT_06_二重の読み取りは1件にまとめ_検証にはまとめる前の一覧を渡し_指摘はまとめた商品に付く()
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
        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.ReviewNote && r.Message == "10袋入と読めます");
        Assert.Contains(item.Reasons, r => r.Key == $"{nameof(FlyerItemReasonCode.DuplicateConflict)}:{FlyerItemField.PriceIncludingTax}");
        Assert.Single(session.RemovedItems);
    }

    [Fact]
    public async Task FR_IMPORT_06_二重に読んだ表記の品目は和集合になる()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー", label: "小麦"), Read("ことりのクッキー", label: "乳")));

        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(new[] { Allergen("wheat").Id, Allergen("milk").Id }.ToHashSet(), item.LabelAllergens.Select(a => a.Id).ToHashSet());
    }

    [Fact]
    public async Task 使えるの確定で_価格と注文番号と載った週は上書きされ_おやつの分類とタグは上書きされない()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット") with { CharacterTags = ["くま"] }));
        var item = await ReadSingleAsync(service, menu);

        var result = await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal(216, existing.Price);
        Assert.Equal(200, existing.PriceExcludingTax);
        Assert.Equal("8101", existing.OrderNo);
        Assert.Equal(menu.WeekStart, existing.ListedWeekStart);
        Assert.Equal(SnackType.Salty, existing.SnackType);
        Assert.Empty(existing.Tags);
        Assert.Equal(existing.Id, Assert.Single(menu.ListedProducts).ProductId);
        // 確認済みの「含む」に収まるので、記録は足さない
        Assert.Empty(data.AddedChecks);
        Assert.Equal(FlyerItemStatus.Confirmed, Assert.Single((await ViewAsync(service, menu)).Items).Status);
    }

    [Fact]
    public async Task 使えるの確定で_過去の週の取り込みでは商品を上書きしない()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.ListedWeekStart = MenuTestData.WeekStart.AddDays(7);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット")));
        var item = await ReadSingleAsync(service, menu);

        await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.Equal(100, existing.Price);
        Assert.Equal("0001", existing.OrderNo);
        Assert.Equal(216, Assert.Single(menu.ListedProducts).Price);
    }

    [Fact]
    public async Task FR_IMPORT_12_内容量と入数がマスタと違えば再入力_このままでよいで使えるになり_確定で内容量と入数を上書きする()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        existing.Volume = "8袋入";
        existing.PiecesPerPack = 8;
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット")));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);
        var reason = Assert.Single(item.Reasons, r => r.Code == FlyerItemReasonCode.MasterVolumeOrPiecesChanged);
        var accepted = await service.AcceptReasonAsync(menu.Id, item.Id, reason.Key);
        Assert.Equal(FlyerItemCategory.Usable, accepted.Value!.Category);

        await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.Equal("10袋入", existing.Volume);
        Assert.Equal(10, existing.PiecesPerPack);
        // 内容量が変わっても同じ商品として確認を引き継ぐ（記録は足さない）
        Assert.Empty(data.AddedChecks);
    }

    [Fact]
    public async Task 一致した商品に記録がないときは_確定でチラシの品目の参考の記録を足す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "くまのビスケット");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", label: "小麦")));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Usable, item.Category);
        var result = await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.True(result.Value!.ConfirmedCount == 1);
        var check = Assert.Single(existing.AllergenChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.Equal(Allergen("wheat").Id, Assert.Single(check.Items).AllergenId);
    }

    [Fact]
    public async Task 使えない_再入力の商品は確定のAPIでも確定できず_何も保存しない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("たまごボーロ", label: "卵"),
            Read("ねこのせんべい", price: null, priceExcludingTax: null)));
        var session = await ReadAllAsync(service, menu);

        var unusable = await service.ConfirmItemAsync(menu.Id, ItemOf(session, "たまごボーロ").Id);
        var needsInput = await service.ConfirmItemAsync(menu.Id, ItemOf(session, "ねこのせんべい").Id);
        var bulkUsable = await service.ConfirmAllUsableAsync(menu.Id, session.Items.Select(i => i.Id).ToList());
        var bulkNew = await service.ConfirmAllNewAsync(menu.Id, session.Items.Select(i => i.Id).ToList());

        Assert.True(unusable.HasError);
        Assert.True(needsInput.HasError);
        Assert.Equal(0, bulkUsable.Value!.ConfirmedCount);
        Assert.Equal(2, bulkUsable.Value.SkippedCount);
        Assert.Equal(0, bulkNew.Value!.ConfirmedCount);
        Assert.Empty(data.Products);
        Assert.Empty(data.AddedChecks);
        Assert.Empty(menu.ListedProducts);
        Assert.Equal(0, data.SaveCount);
    }

    [Fact]
    public async Task SR_20_確認済みの商品でチラシに避ける品目が増えたら使えないになり_最初の確定で参考の記録が足され含むになる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "卵・小麦"), Read("ことりのクッキー")));
        var session = await ReadAllAsync(service, menu);
        var item = ItemOf(session, "さくさくクッキー");

        Assert.Equal(FlyerItemCategory.Unusable, item.Category);
        Assert.Contains(item.Discrepancies, d => d.Allergen.Id == Allergen("egg").Id && d.IsAvoided);
        Assert.Contains(item.Notices, n => n.Code == FlyerItemReasonCode.MasterDiscrepancy);
        // 読み取りが終わった時点では保存しない
        Assert.Empty(data.AddedChecks);

        // ほかの商品の確定で、使えない商品の分もまとめて足す
        var result = await service.ConfirmItemAsync(menu.Id, ItemOf(session, "ことりのクッキー").Id);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        var recorded = Assert.Single(result.Value!.RecordedProducts);
        Assert.Equal(existing.Id, recorded.ProductId);
        Assert.Equal(2, existing.AllergenChecks.Count);
        Assert.Equal(AllergenJudgementKind.Contains, AllergenJudge.Judge(existing, data.Avoided.Select(a => a.Allergen), Today, RecheckDays).Kind);
        Assert.DoesNotContain(menu.ListedProducts, l => l.ProductId == existing.Id);
        Assert.Contains(ItemOf(await ViewAsync(service, menu), "さくさくクッキー").RecordedDiscrepancyAllergens, a => a.Id == Allergen("egg").Id);
    }

    [Fact]
    public async Task SR_20_避ける品目でない品目が増えたら使えるのまま_確定で参考の記録が足され要確認になる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳・小麦")));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Usable, item.Category);
        Assert.Contains(item.Discrepancies, d => d.Allergen.Id == Allergen("milk").Id && !d.IsAvoided);
        Assert.Equal(AllergenJudgementCategory.Safe, JudgeOf(data, existing));

        var result = await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.Single(result.Value!.RecordedProducts);
        var check = Assert.Single(data.AddedChecks);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.All(check.Items, i => Assert.True(i.Contains));
        Assert.Equal(AllergenJudgementCategory.Warning, JudgeOf(data, existing));
        Assert.Equal(existing.Id, Assert.Single(menu.ListedProducts).ProductId);
    }

    [Fact]
    public async Task SR_20_確定の前に職員が表記を直して食い違いがなくなれば_記録は足されない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳・小麦")));
        var item = await ReadSingleAsync(service, menu);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { LabelAllergenIds = [Allergen("wheat").Id] });
        Assert.Empty(edited.Value!.Discrepancies);
        var result = await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.Empty(result.Value!.RecordedProducts);
        Assert.Empty(data.AddedChecks);
        Assert.Single(existing.AllergenChecks);
    }

    [Fact]
    public async Task SR_20_チラシの品目が確認済みの含むの中に収まっていれば_記録は足されない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat", "milk"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "乳")));
        var item = await ReadSingleAsync(service, menu);

        await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.Empty(item.Discrepancies);
        Assert.Empty(data.AddedChecks);
        Assert.Single(existing.AllergenChecks);
    }

    [Fact]
    public async Task SR_20_品目の分からない語が残っていても_対応づけできた品目で食い違いの記録を足す()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("さくさくクッキー", label: "乳・ナゾ成分", unmapped: ["ナゾ成分"]),
            Read("ことりのクッキー")));
        var session = await ReadAllAsync(service, menu);
        Assert.Equal(FlyerItemCategory.NeedsInput, ItemOf(session, "さくさくクッキー").Category);

        await service.ConfirmItemAsync(menu.Id, ItemOf(session, "ことりのクッキー").Id);

        var check = Assert.Single(existing.AllergenChecks, c => c.Status == AllergenCheckStatus.Reference);
        Assert.Contains(check.Items, i => i.AllergenId == Allergen("milk").Id && i.Contains);
    }

    [Fact]
    public async Task SR_20_同じ食い違いの記録は_2回目の確定で足さない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "卵・小麦"), Read("ことりのクッキー"), Read("りすのクッキー")));
        var session = await ReadAllAsync(service, menu);

        await service.ConfirmItemAsync(menu.Id, ItemOf(session, "ことりのクッキー").Id);
        var second = await service.ConfirmItemAsync(menu.Id, ItemOf(session, "りすのクッキー").Id);

        Assert.Empty(second.Value!.RecordedProducts);
        Assert.Equal(2, existing.AllergenChecks.Count);
    }

    [Fact]
    public async Task 確定がエラーになったときは_食い違いの記録も含めて何も保存しない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var existing = Existing(data, "さくさくクッキー", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("さくさくクッキー", label: "卵・小麦"), Read("ねこのせんべい", price: null, priceExcludingTax: null)));
        var session = await ReadAllAsync(service, menu);

        var result = await service.ConfirmItemAsync(menu.Id, ItemOf(session, "ねこのせんべい").Id);

        Assert.True(result.HasError);
        Assert.Single(existing.AllergenChecks);
        Assert.Empty(data.AddedChecks);
        Assert.Equal(0, data.SaveCount);
    }

    [Fact]
    public async Task D_97_表記なしの使えるは_見比べる前はまとめて確定に入らず_見比べたあとは入る()
    {
        var data = new InMemoryData();
        Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), []));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", label: null)));
        var item = await ReadSingleAsync(service, menu);

        Assert.Equal(FlyerItemCategory.Usable, item.Category);
        Assert.Contains(FlyerBulkExclusion.LabelAbsentNotCompared, item.BulkExclusions);
        Assert.Equal(0, (await service.ConfirmAllUsableAsync(menu.Id)).Value!.ConfirmedCount);

        Assert.Equal(1, (await service.MarkLabelAbsentComparedAsync(menu.Id, [item.Id])).Value);
        var result = await service.ConfirmAllUsableAsync(menu.Id);

        Assert.Equal(1, result.Value!.ConfirmedCount);
        Assert.Single(menu.ListedProducts);
    }

    [Fact]
    public async Task 再入力で直して使えるになった商品は_まとめて確定に入る()
    {
        var data = new InMemoryData();
        var existing = Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット", price: null, priceExcludingTax: null)));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PriceIncludingTax = 216, PriceExcludingTax = 200 });

        Assert.Equal(FlyerItemCategory.Usable, edited.Value!.Category);
        Assert.True(edited.Value.CanBulk);
        var result = await service.ConfirmAllUsableAsync(menu.Id, [item.Id]);
        Assert.Equal(1, result.Value!.ConfirmedCount);
        Assert.Equal(216, existing.Price);
    }

    [Fact]
    public async Task 画面で直している途中の行は_まとめの対象から外す()
    {
        var data = new InMemoryData();
        Existing(data, "くまのビスケット", ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("くまのビスケット")));
        var item = await ReadSingleAsync(service, menu);

        var view = await ViewAsync(service, menu, [item.Id]);
        var result = await service.ConfirmAllUsableAsync(menu.Id, [item.Id], [item.Id]);

        Assert.Contains(FlyerBulkExclusion.Editing, Assert.Single(view.Items).BulkExclusions);
        Assert.Equal(0, view.BulkUsable.Count);
        Assert.Contains(view.BulkUsable.Exclusions, e => e.Exclusion == FlyerBulkExclusion.Editing && e.Count == 1);
        Assert.Equal(0, result.Value!.ConfirmedCount);
        Assert.Equal(1, result.Value.SkippedCount);
    }

    [Fact]
    public async Task 新しい商品をまとめて登録は_Claude_の候補で登録し_選んでいない行と条件から外れた行は確定しない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(
            Read("ことりのクッキー") with { CharacterTags = ["ことり"], FlavorTags = [] },
            Read("りすのクッキー"),
            Read("うさぎのクッキー") with { SnackType = null }));
        var session = await ReadAllAsync(service, menu);
        var target = ItemOf(session, "ことりのクッキー");
        var noSnack = ItemOf(session, "うさぎのクッキー");
        Assert.Contains(FlyerBulkExclusion.MissingNewProductFields, noSnack.BulkExclusions);

        var result = await service.ConfirmAllNewAsync(menu.Id, [target.Id, noSnack.Id, "missing"]);

        Assert.Equal(1, result.Value!.ConfirmedCount);
        Assert.Equal(2, result.Value.SkippedCount);
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
    public async Task 新しい商品をまとめて登録した表記なしの商品は_品目が空の参考になり安全にならない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー", label: null)));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(0, (await service.ConfirmAllNewAsync(menu.Id, [item.Id])).Value!.ConfirmedCount);

        await service.MarkLabelAbsentComparedAsync(menu.Id, [item.Id]);
        var result = await service.ConfirmAllNewAsync(menu.Id, [item.Id]);

        Assert.Equal(1, result.Value!.ConfirmedCount);
        var created = Assert.Single(data.Products);
        var check = Assert.Single(created.AllergenChecks);
        Assert.Empty(check.Items);
        Assert.StartsWith("表記なし", check.SourceNote);
        Assert.NotEqual(AllergenJudgementCategory.Safe, JudgeOf(data, created));
    }

    [Fact]
    public async Task 手入力と_読み取りの検証ができなかった取り込みの商品は_まとめに入らない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")), new FixedFlyerReviewer(null));
        var session = await ReadAllAsync(service, menu);
        await service.AddManualAsync(menu.Id, new FlyerManualInput("てがきのせんべい", TestMaker, "10枚", 216, 200, "8102", 10, SnackType.Salty, "小麦"), CancellationToken.None);
        session = await ViewAsync(service, menu);

        Assert.True(session.ReviewFailed);
        Assert.Contains(FlyerBulkExclusion.ReviewFailed, ItemOf(session, "ことりのクッキー").BulkExclusions);
        Assert.Contains(FlyerBulkExclusion.Manual, ItemOf(session, "てがきのせんべい").BulkExclusions);
        Assert.Equal(0, session.BulkNew.Count);
        Assert.Equal(0, (await service.ConfirmAllNewAsync(menu.Id, session.Items.Select(i => i.Id).ToList())).Value!.ConfirmedCount);
    }

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

        Assert.Equal(FlyerItemCategory.Usable, edited.Value!.Category);
        Assert.Equal(existing.Id, edited.Value.MatchedProduct!.Id);
        Assert.Contains(FlyerItemField.Maker, edited.Value.EditedFields);
        Assert.Null(edited.Value.OriginalRead.Maker);
    }

    [Fact]
    public async Task 直した欄の読み取りの検証の指摘は消え_ほかの欄の指摘は残る()
    {
        var data = new InMemoryData();
        var review = new FlyerReview([new FlyerReviewNote(0, "価格", "税込228円と読めます"), new FlyerReviewNote(0, "注文番号", "8110と読めます")], [], []);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")), new FixedFlyerReviewer(review));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(2, item.Reasons.Count(r => r.Code == FlyerItemReasonCode.ReviewNote));

        var edited = await service.UpdateItemAsync(EditOf(menu, item) with { PriceIncludingTax = 228, PriceExcludingTax = 211 });

        var remaining = Assert.Single(edited.Value!.Reasons, r => r.Code == FlyerItemReasonCode.ReviewNote);
        Assert.Equal("8110と読めます", remaining.Message);
        var accepted = await service.AcceptReasonAsync(menu.Id, item.Id, remaining.Key);
        Assert.Equal(FlyerItemCategory.New, accepted.Value!.Category);
        Assert.Equal(216, accepted.Value.OriginalRead.PriceIncludingTax);
    }

    [Fact]
    public async Task このままでよいは_外せない理由には効かない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー", price: null, priceExcludingTax: null)));
        var item = await ReadSingleAsync(service, menu);
        var reason = Assert.Single(item.Reasons, r => r.Code == FlyerItemReasonCode.MissingPrice);

        var accepted = await service.AcceptReasonAsync(menu.Id, item.Id, reason.Key);

        Assert.True(accepted.HasError);
    }

    [Fact]
    public async Task 品目の分からない語に品目を選ぶと分類し直し_避ける品目を選べば使えないになる()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ふしぎなクッキー", label: "小麦・ナゾ成分", unmapped: ["ナゾ成分"])));
        var item = await ReadSingleAsync(service, menu);
        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);
        Assert.True((await service.ConfirmItemAsync(menu.Id, item.Id)).HasError);

        var none = await service.ResolveTermAsync(menu.Id, item.Id, "ナゾ成分", []);
        Assert.Equal(FlyerItemCategory.New, none.Value!.Category);
        Assert.Contains("ナゾ成分", none.Value.UnmappedTerms);

        var egg = await service.ResolveTermAsync(menu.Id, item.Id, "ナゾ成分", [Allergen("egg").Id]);
        Assert.Equal(FlyerItemCategory.Unusable, egg.Value!.Category);
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
    public async Task SR_19_読み取りの検証は表記の品目を足すだけで_減らさず_再入力の理由になる()
    {
        var data = new InMemoryData();
        var review = new FlyerReview([], [], [new FlyerMissedAllergenLabel(0, "乳", "説明文の末尾に（乳）があります")]);
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("のむヨーグルト", label: "小麦")), new FixedFlyerReviewer(review));

        var item = await ReadSingleAsync(service, menu);

        Assert.Contains(item.LabelAllergens, a => a.Name == "乳");
        Assert.Contains(item.LabelAllergens, a => a.Name == "小麦");
        Assert.Equal(FlyerItemCategory.NeedsInput, item.Category);
        Assert.Contains(item.Reasons, r => r.Code == FlyerItemReasonCode.ReviewNote && r.IsAllergenRelated);
    }

    [Fact]
    public async Task 職員が外した表記の品目は_参考の記録に残る()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("たまごボーロ", label: "卵・乳")));
        var item = await ReadSingleAsync(service, menu);

        await service.UpdateItemAsync(EditOf(menu, item) with { LabelAllergenIds = [Allergen("milk").Id] });
        await service.ConfirmItemAsync(menu.Id, item.Id);

        var check = Assert.Single(data.AddedChecks);
        Assert.Contains("職員が外した品目：卵", check.SourceNote);
        Assert.Equal(Allergen("milk").Id, Assert.Single(check.Items).AllergenId);
    }

    [Fact]
    public async Task 確定済みを外すと確定を取り消し_戻して確定し直すと同じ商品を使い直す()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await service.ConfirmItemAsync(menu.Id, item.Id);
        var created = Assert.Single(data.Products);
        Assert.Single(menu.ListedProducts);
        Assert.True((await service.UpdateItemAsync(EditOf(menu, item))).HasError);

        var removed = await service.RemoveItemAsync(menu.Id, item.Id);

        Assert.False(removed.HasError, string.Join(",", removed.ErrorMessages));
        Assert.Empty(menu.ListedProducts);
        Assert.True(created.IsDisabled);

        var restored = await service.RestoreItemAsync(menu.Id, item.Id);
        Assert.Equal(FlyerItemCategory.New, restored.Value!.Category);
        await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.Same(created, Assert.Single(data.Products));
        Assert.False(created.IsDisabled);
        Assert.Equal(created.Id, Assert.Single(menu.ListedProducts).ProductId);
    }

    [Fact]
    public async Task 新しく登録した商品をほかの行も使っているときは_確定を取り消しても使わないにしない()
    {
        var data = new InMemoryData();
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        var item = await ReadSingleAsync(service, menu);
        await service.ConfirmItemAsync(menu.Id, item.Id);
        var created = Assert.Single(data.Products);
        var manual = await service.AddManualAsync(
            menu.Id, new FlyerManualInput("ことりのクッキー", TestMaker, "10袋入", 216, 200, "8101", 10, SnackType.Sweet, "小麦"), CancellationToken.None);
        Assert.False(manual.HasError, string.Join(",", manual.ErrorMessages));
        var manualConfirm = await service.ConfirmItemAsync(menu.Id, manual.Value!);
        Assert.False(manualConfirm.HasError, string.Join(",", manualConfirm.ErrorMessages));

        var removed = await service.RemoveItemAsync(menu.Id, item.Id);

        Assert.False(removed.HasError, string.Join(",", removed.ErrorMessages));
        Assert.False(created.IsDisabled);
        Assert.Equal(created.Id, Assert.Single(menu.ListedProducts).ProductId);
    }

    [Fact]
    public async Task 取り込み直しでは_最初の確定で前の写しを空にする()
    {
        var data = new InMemoryData();
        var old = Existing(data, "むかしのせんべい", ConfirmedAll(Today.AddDays(-5), []));
        var (service, menu, _) = Create(data, new FixedFlyerReader(Read("ことりのクッキー")));
        menu.ListedProducts.Add(new ListedProduct(old.Id, 100, "0001"));
        var item = await ReadSingleAsync(service, menu);

        await service.ConfirmItemAsync(menu.Id, item.Id);

        Assert.DoesNotContain(menu.ListedProducts, l => l.ProductId == old.Id);
        Assert.Single(menu.ListedProducts);
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
