using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>取り込みの分類（D-100、09 の 3・5.2、09 の 10 の「分類」「略記」「確認済みとの食い違い」の分類の部分）</summary>
public class FlyerItemClassifierTests
{
    private static FlyerReadProduct Read(
        string name = "ことりのクッキー",
        string? maker = TestMaker,
        string? volume = "10袋入",
        int? price = 216,
        int? excluding = 200,
        string? orderNo = "8101",
        string? label = "小麦",
        string? description = null,
        int? pieces = 10,
        SnackType? snackType = SnackType.Sweet) => new(
        name, maker, volume, excluding, price, orderNo, null, label, label is null, [], [], description, null, pieces, "10袋入りのため", snackType, [], []);

    private static Product Master(string name = "ことりのクッキー", string? volume = "10袋入", int? pieces = 10, params AllergenCheck[] checks)
    {
        var product = Product(name, checks);
        product.Volume = volume;
        product.PiecesPerPack = pieces;
        return product;
    }

    private static string[] Codes(params string[] codes) => codes.Select(c => Allergen(c).Id).ToArray();

    private static FlyerItemClassification Classify(
        FlyerReadProduct read,
        IEnumerable<Product>? products = null,
        string[]? avoid = null,
        string[]? labelCodes = null,
        string[]? unmapped = null,
        Dictionary<string, IReadOnlyCollection<string>>? resolved = null,
        FlyerReviewNoteInput[]? reviewNotes = null,
        FlyerDuplicateConflict[]? conflicts = null,
        string[]? accepted = null,
        string[]? edited = null,
        FlyerReadProduct? original = null,
        bool isPiecesEnteredByStaff = false,
        bool isManual = false,
        bool reviewFailed = false,
        bool compared = false,
        bool editing = false)
    {
        var avoided = Avoid(avoid ?? ["egg", "walnut"]);
        var match = ProductMatcher.Match(read.Name, read.Maker, products ?? []);
        return FlyerItemClassifier.Classify(new FlyerClassificationInput
        {
            OriginalRead = original ?? read,
            Read = read,
            LabelAllergenIds = Codes(labelCodes ?? ["wheat"]),
            UnmappedTerms = unmapped ?? [],
            ResolvedTerms = resolved ?? new Dictionary<string, IReadOnlyCollection<string>>(),
            ReviewNotes = reviewNotes ?? [],
            DuplicateConflicts = conflicts ?? [],
            AcceptedReasonKeys = (accepted ?? []).ToHashSet(),
            EditedFields = (edited ?? []).ToHashSet(),
            Match = match,
            MatchedJudgement = match.Product is { } p ? AllergenJudge.Judge(p, avoided, Today, RecheckDays) : null,
            AllAllergens = AllAllergens,
            AvoidedAllergens = avoided,
            IsPiecesEnteredByStaff = isPiecesEnteredByStaff,
            IsManual = isManual,
            ReviewFailed = reviewFailed,
            IsLabelAbsentCompared = compared,
            IsEditing = editing,
        });
    }

    private static IReadOnlyList<FlyerItemReasonCode> CodesOf(FlyerItemClassification c) => c.Reasons.Select(r => r.Code).ToList();

    // ---- 4つの分類の基本 ----

    [Fact]
    public void 一致する商品がなく条件に当たらなければ要確認()
    {
        var result = Classify(Read());

        Assert.Equal(FlyerItemCategory.New, result.Category);
        Assert.Empty(result.Reasons);
        Assert.True(result.CanBulk);
    }

    [Fact]
    public void 一致する商品があり条件に当たらなければ使える()
    {
        var result = Classify(Read(), [Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
        Assert.Empty(result.Reasons);
        Assert.True(result.CanBulk);
    }

    // ---- 入数（3.3） ----

    [Fact]
    public void 入数の候補がなく一致した商品にも入数がなければ使えない()
    {
        var none = Classify(Read(pieces: null));
        var masterWithout = Classify(Read(pieces: null), [Master(pieces: null, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);

        Assert.Equal(FlyerItemCategory.Unusable, none.Category);
        Assert.Equal([FlyerItemReasonCode.PiecesUnknown], CodesOf(none));
        Assert.Equal(FlyerItemCategory.Unusable, masterWithout.Category);
        Assert.Null(none.PiecesPerPack);
    }

    [Fact]
    public void 入数の候補がなくても一致した商品に入数があれば使えないにならない()
    {
        var result = Classify(Read(pieces: null), [Master(pieces: 12, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
        Assert.Equal(12, result.PiecesPerPack);
    }

    [Fact]
    public void 入数を入れると分類し直され_まとめに入らない()
    {
        var before = Classify(Read(pieces: null));
        var after = Classify(Read(pieces: 8), original: Read(pieces: null), isPiecesEnteredByStaff: true, edited: [FlyerItemField.PiecesPerPack]);

        Assert.Equal(FlyerItemCategory.Unusable, before.Category);
        Assert.Equal(FlyerItemCategory.New, after.Category);
        Assert.False(after.CanBulk);
        Assert.Contains(FlyerBulkExclusion.PiecesEnteredByStaff, after.BulkExclusions);
    }

    // ---- 避ける品目（3.4） ----

    [Fact]
    public void チラシの表記に避ける品目があれば使えない()
    {
        var result = Classify(Read(label: "卵・小麦"), labelCodes: ["egg", "wheat"]);

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(FlyerItemReasonCode.AvoidedInLabel, reason.Code);
        Assert.Equal("卵を含みます（チラシの表記）", reason.Message);
        Assert.False(reason.CanAccept);
    }

    [Fact]
    public void チラシの説明文に避ける品目の探す言葉があれば使えない()
    {
        var result = Classify(Read(description: "香ばしい胡桃をのせました"));

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Equal([FlyerItemReasonCode.AvoidedInDescription], CodesOf(result));
        Assert.Contains(result.FlyerAllergens, a => a.Id == Allergen("walnut").Id);
    }

    [Fact]
    public void 商品名に避ける品目の探す言葉があれば使えない()
    {
        var result = Classify(Read(name: "クルミのクッキー"));

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Equal([FlyerItemReasonCode.AvoidedInName], CodesOf(result));
        Assert.Contains("「クルミ」", result.Reasons[0].Message);
    }

    [Fact]
    public void 商品名を直して探す言葉を消しても_読んだ商品名で使えないのまま()
    {
        var result = Classify(Read(name: "ことりのクッキー"), original: Read(name: "クルミのクッキー"), edited: [FlyerItemField.Name]);

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Contains(FlyerItemReasonCode.AvoidedInName, CodesOf(result));
    }

    [Fact]
    public void 一致した商品の記録で避ける品目を含めば使えない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat", "egg"]));

        var result = Classify(Read(), [master]);

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(FlyerItemReasonCode.AvoidedInMaster, reason.Code);
        Assert.Equal("卵を含みます（商品一覧の確認の記録）", reason.Message);
    }

    [Fact]
    public void 一致した商品の原材料の全文に探す言葉があれば使えない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"], rawIngredients: "小麦粉、砂糖、ウォールナッツ"));

        var result = Classify(Read(), [master]);

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Equal([FlyerItemReasonCode.AvoidedInMaster], CodesOf(result));
    }

    [Fact]
    public void 一致した商品を使わないにしていれば使えない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        master.IsDisabled = true;

        var result = Classify(Read(), [master]);

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Equal([FlyerItemReasonCode.Disabled], CodesOf(result));
    }

    [Fact]
    public void 避ける品目が1つもなければ_避ける品目による使えないは起きない()
    {
        var result = Classify(Read(name: "クルミのクッキー", label: "卵", description: "胡桃"), labelCodes: ["egg"], avoid: []);

        Assert.Equal(FlyerItemCategory.New, result.Category);
        Assert.Empty(result.Reasons);
    }

    // ---- 分類の順番（3.2） ----

    [Fact]
    public void 税込が読めず避ける品目を含めば使えない_理由はすべて並べアレルゲンを先にする()
    {
        var result = Classify(Read(price: null, label: "卵"), labelCodes: ["egg"]);

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Equal([FlyerItemReasonCode.AvoidedInLabel, FlyerItemReasonCode.MissingPrice], CodesOf(result));
    }

    [Fact]
    public void 入数が分からず税込も読めなければ_アレルゲンでない理由は使えないを先にする()
    {
        var result = Classify(Read(price: null, pieces: null));

        Assert.Equal([FlyerItemReasonCode.PiecesUnknown, FlyerItemReasonCode.MissingPrice], CodesOf(result));
    }

    [Fact]
    public void 税込が読めず一致がなければ再入力()
    {
        var result = Classify(Read(price: null));

        Assert.Equal(FlyerItemCategory.NeedsInput, result.Category);
        Assert.False(result.CanBulk);
        Assert.Empty(result.BulkExclusions);
    }

    // ---- 再入力（3.1） ----

    [Theory]
    [InlineData("price", FlyerItemReasonCode.MissingPrice)]
    [InlineData("tax", FlyerItemReasonCode.TaxMismatch)]
    [InlineData("volume", FlyerItemReasonCode.MissingVolume)]
    [InlineData("orderNo", FlyerItemReasonCode.MissingOrderNo)]
    [InlineData("name", FlyerItemReasonCode.MissingName)]
    [InlineData("maker", FlyerItemReasonCode.MissingMaker)]
    [InlineData("pieces", FlyerItemReasonCode.PiecesMismatch)]
    public void 読み取りに足りない_おかしいところがあれば再入力(string broken, FlyerItemReasonCode expected)
    {
        var read = broken switch
        {
            "price" => Read(price: null),
            "tax" => Read(price: 300),
            "volume" => Read(volume: null),
            "orderNo" => Read(orderNo: " "),
            "name" => Read(name: "　"),
            "maker" => Read(maker: null),
            "pieces" => Read(pieces: 7),
            _ => throw new ArgumentOutOfRangeException(nameof(broken)),
        };

        var result = Classify(read, [Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);

        Assert.Equal(FlyerItemCategory.NeedsInput, result.Category);
        Assert.Contains(expected, CodesOf(result));
    }

    [Fact]
    public void メーカーが空なら照合せず再入力になり_メーカーを入れたら照合し直す()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var missing = Classify(Read(maker: null), [master]);
        var entered = Classify(Read(), [master], original: Read(maker: null), edited: [FlyerItemField.Maker]);

        Assert.Equal(FlyerItemCategory.NeedsInput, missing.Category);
        Assert.Equal([FlyerItemReasonCode.MissingMaker], CodesOf(missing));
        Assert.Equal(FlyerItemCategory.Usable, entered.Category);
    }

    [Fact]
    public void 一致した商品と内容量が違えば再入力で_このままでよいで使えるになる()
    {
        var master = Master(volume: "12袋入", pieces: 12, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var changed = Classify(Read(volume: "10袋入", pieces: 10), [master]);
        var accepted = Classify(Read(volume: "10袋入", pieces: 10), [master], accepted: [nameof(FlyerItemReasonCode.MasterVolumeOrPiecesChanged)]);

        Assert.Equal(FlyerItemCategory.NeedsInput, changed.Category);
        var reason = Assert.Single(changed.Reasons);
        Assert.Equal(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, reason.Code);
        Assert.Contains("届いたらパッケージの原材料も見てください", reason.Message);
        Assert.True(reason.CanAccept);
        Assert.Equal(FlyerItemCategory.Usable, accepted.Category);
        Assert.True(accepted.CanBulk);
    }

    [Fact]
    public void 一致した商品と入数の候補だけが違っても再入力で_このままでよいかマスタと同じ入数に直せば使えるになる()
    {
        var master = Master(volume: "10袋入", pieces: 5, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var changed = Classify(Read(pieces: 10), [master]);
        var accepted = Classify(Read(pieces: 10), [master], accepted: [nameof(FlyerItemReasonCode.MasterVolumeOrPiecesChanged)]);
        var sameAsMaster = Classify(Read(pieces: 5), [master], edited: [FlyerItemField.PiecesPerPack]);

        Assert.Equal([FlyerItemReasonCode.MasterVolumeOrPiecesChanged], CodesOf(changed));
        Assert.DoesNotContain("パッケージの原材料", changed.Reasons[0].Message);
        Assert.Equal(FlyerItemCategory.Usable, accepted.Category);
        Assert.Equal(FlyerItemCategory.Usable, sameAsMaster.Category);
    }

    [Fact]
    public void 入数を入れたり直したりしただけでは_一致した商品との内容量の違いを確かめたことにしない()
    {
        var master = Master(volume: "12袋入", pieces: 12, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var entered = Classify(Read(volume: "10袋入", pieces: 10), [master], isPiecesEnteredByStaff: true);
        var edited = Classify(Read(volume: "10袋入", pieces: 10), [master], edited: [FlyerItemField.PiecesPerPack]);

        Assert.Equal(FlyerItemCategory.NeedsInput, entered.Category);
        Assert.Contains(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, CodesOf(entered));
        Assert.Equal(FlyerItemCategory.NeedsInput, edited.Category);
        Assert.Contains(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, CodesOf(edited));
    }

    [Fact]
    public void 内容量の書き方の違いだけなら再入力にしない()
    {
        var master = Master(volume: "40g（2袋×12個包装）", pieces: 24, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(volume: "40g(2袋x12個包装)", pieces: 24), [master]);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
    }

    [Fact]
    public void 品目の分からない語があれば再入力で_品目を選ぶと分類し直す()
    {
        var unresolved = Classify(Read(label: "小麦・ﾅｿ"), unmapped: ["ﾅｿ"]);
        var none = Classify(Read(label: "小麦・ﾅｿ"), unmapped: ["ﾅｿ"], resolved: new() { ["ﾅｿ"] = [] });
        var egg = Classify(Read(label: "小麦・ﾅｿ"), unmapped: ["ﾅｿ"], resolved: new() { ["ﾅｿ"] = Codes("egg") });

        Assert.Equal(FlyerItemCategory.NeedsInput, unresolved.Category);
        var reason = Assert.Single(unresolved.Reasons);
        Assert.Equal(FlyerItemReasonCode.UnmappedTerm, reason.Code);
        Assert.False(reason.CanAccept);
        Assert.Equal(["ﾅｿ"], unresolved.UnresolvedTerms);
        Assert.Equal(FlyerItemCategory.New, none.Category);
        Assert.Equal(FlyerItemCategory.Unusable, egg.Category);
        Assert.Equal([FlyerItemReasonCode.AvoidedInLabel], CodesOf(egg));
    }

    [Fact]
    public void 品目の分からない語は_このままでよいでは外せない()
    {
        var result = Classify(Read(label: "ﾅｿ"), labelCodes: [], unmapped: ["ﾅｿ"], accepted: [$"{FlyerItemReasonCode.UnmappedTerm}:ﾅｿ"]);

        Assert.Equal(FlyerItemCategory.NeedsInput, result.Category);
    }

    [Fact]
    public void 読み取りの検証の指摘があれば再入力で_このままでよいで消える()
    {
        var note = new FlyerReviewNoteInput("review:0", "税込価格", "税込価格が 216 円か 261 円か読みにくいです");

        var noted = Classify(Read(), reviewNotes: [note]);
        var accepted = Classify(Read(), reviewNotes: [note], accepted: ["review:0"]);

        Assert.Equal(FlyerItemCategory.NeedsInput, noted.Category);
        Assert.Equal(FlyerItemReasonCode.ReviewNote, Assert.Single(noted.Reasons).Code);
        Assert.Equal(FlyerItemCategory.New, accepted.Category);
    }

    [Fact]
    public void 読めない値は_このままでよいでは外せない()
    {
        var result = Classify(Read(price: null), accepted: [nameof(FlyerItemReasonCode.MissingPrice)]);

        Assert.Equal(FlyerItemCategory.NeedsInput, result.Category);
    }

    [Fact]
    public void 二重に読んだ商品で値が食い違えば再入力で_直せば消える()
    {
        var conflict = new FlyerDuplicateConflict(FlyerItemField.PriceIncludingTax, ["216", "270"]);

        var conflicted = Classify(Read(), conflicts: [conflict]);
        var edited = Classify(Read(), conflicts: [conflict], edited: [FlyerItemField.PriceIncludingTax]);

        Assert.Equal(FlyerItemCategory.NeedsInput, conflicted.Category);
        Assert.Contains("216／270", Assert.Single(conflicted.Reasons).Message);
        Assert.Equal(FlyerItemCategory.New, edited.Category);
    }

    [Fact]
    public void 表記なしと読んだのに一致した商品の有効な記録で特定原材料を含めば再入力()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat", "milk"]));

        var result = Classify(Read(label: null), [master], labelCodes: []);

        Assert.Equal(FlyerItemCategory.NeedsInput, result.Category);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(FlyerItemReasonCode.LabelAbsentButMasterContains, reason.Code);
        Assert.True(reason.IsAllergenRelated);
        Assert.Contains("小麦・乳", reason.Message);
    }

    [Fact]
    public void 表記なしと読み_一致した商品の記録が準ずるものだけなら再入力にしない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["almond"]));

        var result = Classify(Read(label: null), [master], labelCodes: []);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
        Assert.True(result.IsLabelAbsent);
    }

    // ---- 一致した商品の記録のステータス（3.1 の注・3.2 の注） ----

    [Fact]
    public void 一致した商品の記録が参考でも_避ける品目がなければ使える_判定は警告のまま()
    {
        var master = Master(checks: Reference(Today.AddDays(-5), "wheat"));
        var avoided = Avoid("egg", "walnut");

        var result = Classify(Read(), [master]);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
        Assert.Equal(AllergenJudgementCategory.Warning, AllergenJudge.Judge(master, avoided, Today, RecheckDays).Category);
    }

    [Fact]
    public void 一致した商品が要再確認でも_避ける品目がなければ使える()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-(RecheckDays + 1)), ["wheat"]));

        var result = Classify(Read(), [master]);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
        Assert.Equal(AllergenJudgementKind.RecheckNeeded, AllergenJudge.Judge(master, Avoid("egg", "walnut"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void 一致した商品に記録がないとき_チラシに避ける品目がなければ使え_あれば使えない()
    {
        var safe = Classify(Read(), [Master()]);
        var blocked = Classify(Read(label: "卵"), [Master()], labelCodes: ["egg"]);

        Assert.Equal(FlyerItemCategory.Usable, safe.Category);
        Assert.Equal(FlyerItemCategory.Unusable, blocked.Category);
        Assert.Equal([FlyerItemReasonCode.AvoidedInLabel], CodesOf(blocked));
    }

    // ---- 確認済みとの食い違い（3.5） ----

    [Fact]
    public void 確認済みの商品でチラシに避ける品目が増えたら使えない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(label: "卵・小麦"), [master], labelCodes: ["egg", "wheat"]);

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Contains(result.Discrepancies, d => d.Allergen.Id == Allergen("egg").Id && d.IsAvoided);
        Assert.Equal(FlyerItemReasonCode.MasterDiscrepancy, Assert.Single(result.Notices).Code);
    }

    [Fact]
    public void 確認済みの商品で避ける品目でない品目が増えたら使えるのまま食い違いを知らせる()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(label: "乳・小麦"), [master], labelCodes: ["milk", "wheat"]);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
        var discrepancy = Assert.Single(result.Discrepancies);
        Assert.Equal(Allergen("milk").Id, discrepancy.Allergen.Id);
        Assert.False(discrepancy.IsAvoided);
        Assert.Contains("乳", Assert.Single(result.Notices).Message);
    }

    [Fact]
    public void 確認済みの含むの中に収まっていれば食い違いはない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat", "milk"]));

        var result = Classify(Read(label: "小麦"), [master]);

        Assert.Empty(result.Discrepancies);
        Assert.Empty(result.Notices);
    }

    // ---- 略記（4） ----

    [Fact]
    public void Claude_が品目を返さなくても表記のカからカシューナッツが当たり使えない()
    {
        var parsed = FlyerAllergenPlanner.ParseLabel("乳・カ", AllAllergens);

        var result = Classify(Read(label: "乳・カ"), avoid: ["cashew"], labelCodes: parsed.Allergens.Select(a => AllAllergens.Single(x => x.Id == a.Id).Code).ToArray());

        Assert.Equal(FlyerItemCategory.Unusable, result.Category);
        Assert.Equal("カシューナッツを含みます（チラシの表記）", Assert.Single(result.Reasons).Message);
    }

    [Fact]
    public void ひらがなのかはかにで_カシューナッツを避けていても使えないにしない()
    {
        var parsed = FlyerAllergenPlanner.ParseLabel("乳・か", AllAllergens);

        var result = Classify(Read(label: "乳・か"), avoid: ["cashew"], labelCodes: parsed.Allergens.Select(a => AllAllergens.Single(x => x.Id == a.Id).Code).ToArray());

        Assert.Equal(FlyerItemCategory.New, result.Category);
        Assert.Contains(result.FlyerAllergens, a => a.Id == Allergen("crab").Id);
    }

    // ---- まとめに入れない商品（5.2） ----

    [Fact]
    public void 表記なしは見比べの印を付けるまでまとめに入らない()
    {
        var notCompared = Classify(Read(label: null), labelCodes: []);
        var compared = Classify(Read(label: null), labelCodes: [], compared: true);

        Assert.Equal(FlyerItemCategory.New, notCompared.Category);
        Assert.True(notCompared.CanMarkLabelAbsentCompared);
        Assert.Equal([FlyerBulkExclusion.LabelAbsentNotCompared], notCompared.BulkExclusions);
        Assert.True(compared.CanBulk);
    }

    [Fact]
    public void 手入力_検証の失敗_直している途中の商品はまとめに入らない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(), [master], isManual: true, reviewFailed: true, editing: true);

        Assert.Equal(FlyerItemCategory.Usable, result.Category);
        Assert.False(result.CanBulk);
        Assert.Equal([FlyerBulkExclusion.Manual, FlyerBulkExclusion.ReviewFailed, FlyerBulkExclusion.Editing], result.BulkExclusions);
        Assert.False(Classify(Read(label: null), labelCodes: [], reviewFailed: true).CanMarkLabelAbsentCompared);
    }

    [Fact]
    public void 要確認でおやつの分類がない商品はまとめて登録に入らない()
    {
        var result = Classify(Read(snackType: null));

        Assert.Equal(FlyerItemCategory.New, result.Category);
        Assert.Equal([FlyerBulkExclusion.MissingNewProductFields], result.BulkExclusions);
    }

    [Fact]
    public void 再入力で直したあとに使える_要確認になった商品はまとめに入る()
    {
        var result = Classify(Read(price: 216), original: Read(price: null), edited: [FlyerItemField.PriceIncludingTax]);

        Assert.Equal(FlyerItemCategory.New, result.Category);
        Assert.True(result.CanBulk);
    }

    [Fact]
    public void 一致したのに判定を渡さなければ例外にする()
    {
        var master = Master();
        var input = new FlyerClassificationInput
        {
            OriginalRead = Read(),
            Read = Read(),
            Match = ProductMatcher.Match("ことりのクッキー", TestMaker, [master]),
            AllAllergens = AllAllergens,
            AvoidedAllergens = Avoid("egg"),
        };

        Assert.Throws<ArgumentException>(() => FlyerItemClassifier.Classify(input));
    }
}
