using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>取り込みの分類（D-101、09 の 3・3.8、09 の 10 の「分類」「略記」「確認済みとの食い違い」の分類の部分）</summary>
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
        string[]? edited = null,
        FlyerReadProduct? original = null,
        bool isPiecesEnteredByStaff = false,
        bool isManual = false,
        bool overwritesMaster = true,
        Product? ownCreated = null)
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
            EditedFields = (edited ?? []).ToHashSet(),
            Match = match,
            MatchedJudgement = match.Product is { } p ? AllergenJudge.Judge(p, avoided, Today, RecheckDays) : null,
            OwnCreatedJudgement = ownCreated is null ? null : AllergenJudge.Judge(ownCreated, avoided, Today, RecheckDays),
            AllAllergens = AllAllergens,
            AvoidedAllergens = avoided,
            IsPiecesEnteredByStaff = isPiecesEnteredByStaff,
            IsManual = isManual,
            OverwritesMaster = overwritesMaster,
        });
    }

    private static IReadOnlyList<FlyerItemReasonCode> CodesOf(FlyerItemClassification c) => c.Reasons.Select(r => r.Code).ToList();

    private static IReadOnlyList<FlyerItemReasonCode> NoteCodesOf(FlyerItemClassification c) => c.Notes.Select(r => r.Code).ToList();

    // ---- 3つの分類の基本 ----

    [Fact]
    public void 一致する商品がなく条件に当たらなければ取り込めるで新しい商品()
    {
        var result = Classify(Read());

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.True(result.IsNew);
        Assert.Empty(result.Reasons);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void 一致する商品があり条件に当たらなければ取り込めるで既存の商品()
    {
        var result = Classify(Read(), [Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.False(result.IsNew);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void 分類は使えない_再入力_取り込めるの3つだけ()
    {
        Assert.Equal(
            [FlyerItemCategory.Unusable, FlyerItemCategory.NeedsInput, FlyerItemCategory.Ready],
            Enum.GetValues<FlyerItemCategory>());
    }

    [Theory]
    [InlineData(FlyerItemReasonCode.PiecesUnknown, FlyerItemReasonLevel.Unusable)]
    [InlineData(FlyerItemReasonCode.AvoidedInLabel, FlyerItemReasonLevel.Unusable)]
    [InlineData(FlyerItemReasonCode.AvoidedInDescription, FlyerItemReasonLevel.Unusable)]
    [InlineData(FlyerItemReasonCode.AvoidedInName, FlyerItemReasonLevel.Unusable)]
    [InlineData(FlyerItemReasonCode.AvoidedInMaster, FlyerItemReasonLevel.Unusable)]
    [InlineData(FlyerItemReasonCode.Disabled, FlyerItemReasonLevel.Unusable)]
    [InlineData(FlyerItemReasonCode.MissingName, FlyerItemReasonLevel.NeedsInput)]
    [InlineData(FlyerItemReasonCode.MissingMaker, FlyerItemReasonLevel.NeedsInput)]
    [InlineData(FlyerItemReasonCode.MissingPrice, FlyerItemReasonLevel.NeedsInput)]
    [InlineData(FlyerItemReasonCode.UnmappedTerm, FlyerItemReasonLevel.NeedsInput)]
    [InlineData(FlyerItemReasonCode.LabelAbsentButMasterContains, FlyerItemReasonLevel.NeedsInput)]
    [InlineData(FlyerItemReasonCode.MissingSnackType, FlyerItemReasonLevel.NeedsInput)]
    [InlineData(FlyerItemReasonCode.MasterDiscrepancy, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.ReviewNote, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.DuplicateConflict, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.TaxMismatch, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.MissingVolume, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.MissingOrderNo, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.PiecesMismatch, FlyerItemReasonLevel.Note)]
    [InlineData(FlyerItemReasonCode.NotReviewed, FlyerItemReasonLevel.Note)]
    public void 理由の種類ごとの重さが決まっていて_注意は分類を変えない(FlyerItemReasonCode code, FlyerItemReasonLevel level)
    {
        var master = Master(volume: "12袋入", pieces: 12, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));
        var result = code switch
        {
            FlyerItemReasonCode.PiecesUnknown => Classify(Read(pieces: null)),
            FlyerItemReasonCode.AvoidedInLabel => Classify(Read(label: "卵"), labelCodes: ["egg"]),
            FlyerItemReasonCode.AvoidedInDescription => Classify(Read(description: "胡桃入り")),
            FlyerItemReasonCode.AvoidedInName => Classify(Read(name: "クルミのクッキー")),
            FlyerItemReasonCode.AvoidedInMaster => Classify(Read(), [Master(checks: ConfirmedAll(Today.AddDays(-5), ["egg"]))]),
            FlyerItemReasonCode.Disabled => Classify(Read(), [DisabledMaster()]),
            FlyerItemReasonCode.MissingName => Classify(Read(name: " ")),
            FlyerItemReasonCode.MissingMaker => Classify(Read(maker: null)),
            FlyerItemReasonCode.MissingPrice => Classify(Read(price: null)),
            FlyerItemReasonCode.UnmappedTerm => Classify(Read(label: "ﾅｿ"), labelCodes: [], unmapped: ["ﾅｿ"]),
            FlyerItemReasonCode.LabelAbsentButMasterContains => Classify(Read(label: null), [Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))], labelCodes: []),
            FlyerItemReasonCode.MissingSnackType => Classify(Read(snackType: null)),
            FlyerItemReasonCode.MasterDiscrepancy => Classify(Read(label: "乳・小麦"), [Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))], labelCodes: ["milk", "wheat"]),
            FlyerItemReasonCode.ReviewNote => Classify(Read(), reviewNotes: [new FlyerReviewNoteInput("review:0", "税込価格", "読みにくいです")]),
            FlyerItemReasonCode.MasterVolumeOrPiecesChanged => Classify(Read(), [master]),
            FlyerItemReasonCode.DuplicateConflict => Classify(Read(), conflicts: [new FlyerDuplicateConflict(FlyerItemField.PriceIncludingTax, ["216", "270"])]),
            FlyerItemReasonCode.TaxMismatch => Classify(Read(price: 300)),
            FlyerItemReasonCode.MissingVolume => Classify(Read(volume: null)),
            FlyerItemReasonCode.MissingOrderNo => Classify(Read(orderNo: " ")),
            FlyerItemReasonCode.PiecesMismatch => Classify(Read(pieces: 7)),
            FlyerItemReasonCode.NotReviewed => Classify(Read(), isManual: true),
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };

        var found = result.Reasons.Concat(result.Notes).First(r => r.Code == code);
        Assert.Equal(level, found.Level);
        var expected = level switch
        {
            FlyerItemReasonLevel.Unusable => FlyerItemCategory.Unusable,
            FlyerItemReasonLevel.NeedsInput => FlyerItemCategory.NeedsInput,
            _ => FlyerItemCategory.Ready,
        };
        Assert.Equal(expected, result.Category);
        Assert.All(result.Notes, n => Assert.Equal(FlyerItemReasonLevel.Note, n.Level));
        Assert.All(result.Reasons, r => Assert.NotEqual(FlyerItemReasonLevel.Note, r.Level));

        static Product DisabledMaster()
        {
            var disabled = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));
            disabled.IsDisabled = true;
            return disabled;
        }
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

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Equal(12, result.PiecesPerPack);
    }

    [Fact]
    public void 入数を入れると取り込めるになり_検証を通っていない注意が出る()
    {
        var before = Classify(Read(pieces: null));
        var after = Classify(Read(pieces: 8), original: Read(pieces: null), isPiecesEnteredByStaff: true, edited: [FlyerItemField.PiecesPerPack]);

        Assert.Equal(FlyerItemCategory.Unusable, before.Category);
        Assert.Equal(FlyerItemCategory.Ready, after.Category);
        var note = Assert.Single(after.Notes, n => n.Code == FlyerItemReasonCode.NotReviewed);
        Assert.Equal("入数は職員が入れました。読み取りの検証を通っていません", note.Message);
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
    public void この行で登録して使わないに戻した商品の記録で避ける品目を含めば_照合から外しても使えない()
    {
        // 決めたこと H：パッケージで確かめて卵を含むと分かった商品
        var own = Master(checks: ConfirmedAll(Today.AddDays(-1), ["wheat", "egg"]));
        own.IsDisabled = true;

        var blocked = Classify(Read(), ownCreated: own);
        var safeOwn = Classify(Read(), ownCreated: Master(checks: ConfirmedAll(Today.AddDays(-1), ["wheat"])));

        Assert.Equal(FlyerItemCategory.Unusable, blocked.Category);
        var reason = Assert.Single(blocked.Reasons);
        Assert.Equal(FlyerItemReasonCode.AvoidedInMaster, reason.Code);
        Assert.Equal("この取り込みで登録した商品の記録で卵を含みます", reason.Message);
        Assert.Equal(FlyerItemCategory.Ready, safeOwn.Category);
    }

    [Fact]
    public void 避ける品目が1つもなければ_避ける品目による使えないは起きない()
    {
        var result = Classify(Read(name: "クルミのクッキー", label: "卵", description: "胡桃"), labelCodes: ["egg"], avoid: []);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
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
    public void 使えない_再入力_取り込めるの順で決まり_注意だけなら取り込める()
    {
        var needsInput = Classify(Read(price: null, volume: null, orderNo: null));
        var notesOnly = Classify(Read(price: 300, volume: null, orderNo: null, pieces: 10));

        Assert.Equal(FlyerItemCategory.NeedsInput, needsInput.Category);
        Assert.Equal([FlyerItemReasonCode.MissingPrice], CodesOf(needsInput));
        Assert.Equal(FlyerItemCategory.Ready, notesOnly.Category);
        Assert.Empty(notesOnly.Reasons);
        Assert.Equal(
            [FlyerItemReasonCode.TaxMismatch, FlyerItemReasonCode.MissingVolume, FlyerItemReasonCode.MissingOrderNo],
            NoteCodesOf(notesOnly));
    }

    // ---- 再入力（3.1 の6つ） ----

    [Theory]
    [InlineData("price", FlyerItemReasonCode.MissingPrice)]
    [InlineData("name", FlyerItemReasonCode.MissingName)]
    [InlineData("maker", FlyerItemReasonCode.MissingMaker)]
    public void 商品名_メーカー_税込が読めなければ再入力(string broken, FlyerItemReasonCode expected)
    {
        var read = broken switch
        {
            "price" => Read(price: null),
            "name" => Read(name: "　"),
            "maker" => Read(maker: null),
            _ => throw new ArgumentOutOfRangeException(nameof(broken)),
        };

        var result = Classify(read, [Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);

        Assert.Equal(FlyerItemCategory.NeedsInput, result.Category);
        Assert.Contains(expected, CodesOf(result));
    }

    [Theory]
    [InlineData("tax", FlyerItemReasonCode.TaxMismatch, "税込300円が税抜200円と合いません。税込300円を使います")]
    [InlineData("volume", FlyerItemReasonCode.MissingVolume, "内容量が読めませんでした")]
    [InlineData("orderNo", FlyerItemReasonCode.MissingOrderNo, "注文番号が読めませんでした。注文のときにチラシで確かめてください")]
    [InlineData("pieces", FlyerItemReasonCode.PiecesMismatch, "入数の候補（7）が内容量の表記と合いません。7 を使います")]
    public void 税込と税抜が合わない_内容量や注文番号が読めない_入数の候補が合わないは注意で取り込める(string broken, FlyerItemReasonCode expected, string message)
    {
        var read = broken switch
        {
            "tax" => Read(price: 300),
            "volume" => Read(volume: null),
            "orderNo" => Read(orderNo: " "),
            "pieces" => Read(volume: "10袋入", pieces: 7),
            _ => throw new ArgumentOutOfRangeException(nameof(broken)),
        };

        var result = Classify(read, [Master(volume: read.Volume, pieces: read.PiecesPerPackCandidate, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Empty(result.Reasons);
        var note = Assert.Single(result.Notes);
        Assert.Equal(expected, note.Code);
        Assert.Equal(message, note.Message);
    }

    [Fact]
    public void 入数の候補が合わなくても_職員が入数を直せば注意は出ない()
    {
        var result = Classify(Read(pieces: 7), edited: [FlyerItemField.PiecesPerPack]);

        Assert.DoesNotContain(FlyerItemReasonCode.PiecesMismatch, NoteCodesOf(result));
    }

    [Fact]
    public void メーカーが空なら照合せず再入力になり_メーカーを入れたら照合し直す()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var missing = Classify(Read(maker: null), [master]);
        var entered = Classify(Read(), [master], original: Read(maker: null), edited: [FlyerItemField.Maker]);

        Assert.Equal(FlyerItemCategory.NeedsInput, missing.Category);
        Assert.Equal([FlyerItemReasonCode.MissingMaker], CodesOf(missing));
        Assert.Equal(FlyerItemCategory.Ready, entered.Category);
        Assert.False(entered.IsNew);
    }

    [Fact]
    public void 新しい商品でおやつの分類がなければ再入力_一致した商品があれば分類がなくても取り込める()
    {
        var newProduct = Classify(Read(snackType: null));
        var existing = Classify(Read(snackType: null), [Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]))]);
        var chosen = Classify(Read(snackType: SnackType.Salty), original: Read(snackType: null));

        Assert.Equal(FlyerItemCategory.NeedsInput, newProduct.Category);
        var reason = Assert.Single(newProduct.Reasons);
        Assert.Equal(FlyerItemReasonCode.MissingSnackType, reason.Code);
        Assert.Equal(FlyerItemField.SnackType, reason.Field);
        Assert.Equal(FlyerItemCategory.Ready, existing.Category);
        Assert.Equal(FlyerItemCategory.Ready, chosen.Category);
    }

    // ---- 内容量・入数がマスタと違う（注意。FR-IMPORT-12） ----

    [Fact]
    public void 一致した商品と内容量が違えば注意で取り込め_原材料も見てくださいを添える()
    {
        var master = Master(volume: "12袋入", pieces: 12, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(volume: "10袋入", pieces: 10), [master]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Empty(result.Reasons);
        var note = Assert.Single(result.Notes);
        Assert.Equal(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, note.Code);
        Assert.Equal(
            "内容量が変わりました（商品一覧 12袋入・12個 → チラシ 10袋入・10個）。チラシの値で商品一覧を上書きします。届いたらパッケージの原材料も見てください",
            note.Message);
    }

    [Fact]
    public void 一致した商品と入数だけが違えば原材料の文は添えない()
    {
        var master = Master(volume: "10袋入", pieces: 5, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var changed = Classify(Read(pieces: 10), [master]);
        var sameAsMaster = Classify(Read(pieces: 5), [master], edited: [FlyerItemField.PiecesPerPack]);

        Assert.Equal(FlyerItemCategory.Ready, changed.Category);
        var note = Assert.Single(changed.Notes);
        Assert.Equal("入数が変わりました（商品一覧 5個 → チラシ 10個）。チラシの値で商品一覧を上書きします", note.Message);
        Assert.DoesNotContain("原材料", note.Message);
        Assert.Empty(sameAsMaster.Notes);
    }

    [Fact]
    public void 過去の週の取り込みなら_上書きしないと知らせる()
    {
        var master = Master(volume: "12袋入", pieces: 12, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(volume: "10袋入", pieces: 10), [master], overwritesMaster: false);

        var note = Assert.Single(result.Notes);
        Assert.Contains("過去の週の取り込みなので、商品一覧は上書きしません（空の項目だけ埋めます）", note.Message);
        Assert.DoesNotContain("チラシの値で商品一覧を上書きします", note.Message);
    }

    [Fact]
    public void 入数を入れたり直したりしても_内容量の違いが残る限り注意は出る()
    {
        var master = Master(volume: "12袋入", pieces: 12, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var entered = Classify(Read(volume: "10袋入", pieces: 10), [master], isPiecesEnteredByStaff: true);
        var edited = Classify(Read(volume: "10袋入", pieces: 10), [master], edited: [FlyerItemField.PiecesPerPack]);

        Assert.Equal(FlyerItemCategory.Ready, entered.Category);
        Assert.Contains(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, NoteCodesOf(entered));
        Assert.Contains(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, NoteCodesOf(edited));
    }

    [Fact]
    public void 内容量の書き方の違いだけなら注意も出さない()
    {
        var master = Master(volume: "40g（2袋×12個包装）", pieces: 24, checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(volume: "40g(2袋x12個包装)", pieces: 24), [master]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Empty(result.Notes);
    }

    // ---- 品目の分からない語（再入力） ----

    [Fact]
    public void 品目の分からない語があれば再入力で_品目を選ぶと分類し直す()
    {
        var unresolved = Classify(Read(label: "小麦・ﾅｿ"), unmapped: ["ﾅｿ"]);
        var none = Classify(Read(label: "小麦・ﾅｿ"), unmapped: ["ﾅｿ"], resolved: new() { ["ﾅｿ"] = [] });
        var egg = Classify(Read(label: "小麦・ﾅｿ"), unmapped: ["ﾅｿ"], resolved: new() { ["ﾅｿ"] = Codes("egg") });

        Assert.Equal(FlyerItemCategory.NeedsInput, unresolved.Category);
        var reason = Assert.Single(unresolved.Reasons);
        Assert.Equal(FlyerItemReasonCode.UnmappedTerm, reason.Code);
        Assert.Equal(["ﾅｿ"], unresolved.UnresolvedTerms);
        Assert.Equal(FlyerItemCategory.Ready, none.Category);
        Assert.Equal(FlyerItemCategory.Unusable, egg.Category);
        Assert.Equal([FlyerItemReasonCode.AvoidedInLabel], CodesOf(egg));
    }

    // ---- 読み取りの検証・二重の読み取り（注意） ----

    [Fact]
    public void 読み取りの検証の指摘は注意で取り込め_アレルゲンの指摘は注意の先頭に並ぶ()
    {
        var price = new FlyerReviewNoteInput("review:0", "税込価格", "税込価格が 216 円か 261 円か読みにくいです");
        var allergen = new FlyerReviewNoteInput("review:1", "アレルゲン", "アレルゲンの表記「卵」を読み落としている可能性があります");

        var result = Classify(Read(volume: null), reviewNotes: [price, allergen]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Empty(result.Reasons);
        Assert.Equal(
            [FlyerItemReasonCode.ReviewNote, FlyerItemReasonCode.ReviewNote, FlyerItemReasonCode.MissingVolume],
            NoteCodesOf(result));
        Assert.Equal("review:1", result.Notes[0].Key);
        Assert.True(result.Notes[0].IsAllergenRelated);
        Assert.Equal("税込価格が 216 円か 261 円か読みにくいです", result.Notes[1].Message);
    }

    [Fact]
    public void 読み取りの検証の指摘があっても_表記の品目は消えない()
    {
        // SR-19：検証は品目を減らさない。分類の入力の品目がそのままチラシの品目に残る
        var note = new FlyerReviewNoteInput("review:0", "アレルゲン", "アレルゲンの表記は「なし」かもしれません");

        var result = Classify(Read(label: "乳・小麦"), labelCodes: ["milk", "wheat"], reviewNotes: [note]);

        Assert.Equal(Codes("wheat", "milk").Order(), result.FlyerAllergens.Select(a => a.Id).Order());
        Assert.False(result.IsLabelAbsent);
    }

    [Fact]
    public void 二重に読んだ商品で値が食い違えば注意で先に読んだ値を使い_直せば注意は消える()
    {
        var conflict = new FlyerDuplicateConflict(FlyerItemField.PriceIncludingTax, ["216", "270"]);
        var orderNo = new FlyerDuplicateConflict(FlyerItemField.OrderNo, ["8101", "8102"]);

        var conflicted = Classify(Read(), conflicts: [conflict, orderNo]);
        var edited = Classify(Read(), conflicts: [conflict], edited: [FlyerItemField.PriceIncludingTax]);

        Assert.Equal(FlyerItemCategory.Ready, conflicted.Category);
        Assert.Equal("2回読みました。税込価格が 216 / 270 と違います。216 を使います", conflicted.Notes[0].Message);
        Assert.Contains("同じ名前・メーカーで大きさ違いの商品が載っているかもしれません", conflicted.Notes[1].Message);
        Assert.Equal(FlyerItemCategory.Ready, edited.Category);
        Assert.Empty(edited.Notes);
    }

    [Fact]
    public void 手入力の商品は注意で取り込める()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(), [master], isManual: true);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        var note = Assert.Single(result.Notes);
        Assert.Equal(FlyerItemReasonCode.NotReviewed, note.Code);
        Assert.Equal("手で入力した商品です。読み取りの検証を通っていません", note.Message);
    }

    // ---- 表記なし（決めたこと E） ----

    [Fact]
    public void 表記の品目も分からない語もなければ表記なし_品目が1つでもあれば表記なしでない()
    {
        var absent = Classify(Read(label: null), labelCodes: []);
        var noneText = Classify(Read(label: "なし"), labelCodes: []);
        var withLabel = Classify(Read(label: null), labelCodes: ["milk"]);
        var withTerm = Classify(Read(label: "ﾅｿ"), labelCodes: [], unmapped: ["ﾅｿ"]);
        var resolvedMilk = Classify(Read(label: "ﾅｿ"), labelCodes: [], unmapped: ["ﾅｿ"], resolved: new() { ["ﾅｿ"] = Codes("milk") });

        Assert.True(absent.IsLabelAbsent);
        Assert.Equal(FlyerItemCategory.Ready, absent.Category);
        Assert.Empty(absent.FlyerAllergens);
        Assert.True(noneText.IsLabelAbsent);
        Assert.False(withLabel.IsLabelAbsent);
        Assert.False(withTerm.IsLabelAbsent);
        Assert.False(resolvedMilk.IsLabelAbsent);
    }

    [Fact]
    public void 表記なしと読んだのに一致した商品の有効な記録で特定原材料を含めば再入力で_品目を入れれば取り込める()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat", "milk"]));

        var result = Classify(Read(label: null), [master], labelCodes: []);
        var entered = Classify(Read(label: null), [master], labelCodes: ["wheat"], edited: [FlyerItemField.AllergenLabel]);

        Assert.Equal(FlyerItemCategory.NeedsInput, result.Category);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(FlyerItemReasonCode.LabelAbsentButMasterContains, reason.Code);
        Assert.True(reason.IsAllergenRelated);
        Assert.Contains("小麦・乳", reason.Message);
        Assert.Contains("「外す」", reason.Message);
        Assert.Equal(Codes("wheat", "milk"), reason.Allergens.Select(a => a.Id));
        Assert.Equal(FlyerItemCategory.Ready, entered.Category);
    }

    [Fact]
    public void 表記なしと読み_一致した商品の記録が準ずるものだけなら再入力にしない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["almond"]));

        var result = Classify(Read(label: null), [master], labelCodes: []);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.True(result.IsLabelAbsent);
    }

    // ---- 一致した商品の記録のステータス（3.1 の注・3.2 の注） ----

    [Fact]
    public void 一致した商品の記録が参考でも_避ける品目がなければ取り込める_判定は警告のまま()
    {
        var master = Master(checks: Reference(Today.AddDays(-5), "wheat"));
        var avoided = Avoid("egg", "walnut");

        var result = Classify(Read(), [master]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Equal(AllergenJudgementCategory.Warning, AllergenJudge.Judge(master, avoided, Today, RecheckDays).Category);
    }

    [Fact]
    public void 一致した商品が要再確認でも_避ける品目がなければ取り込める()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-(RecheckDays + 1)), ["wheat"]));

        var result = Classify(Read(), [master]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Equal(AllergenJudgementKind.RecheckNeeded, AllergenJudge.Judge(master, Avoid("egg", "walnut"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void 一致した商品に記録がないとき_チラシに避ける品目がなければ取り込め_あれば使えない()
    {
        var safe = Classify(Read(), [Master()]);
        var blocked = Classify(Read(label: "卵"), [Master()], labelCodes: ["egg"]);

        Assert.Equal(FlyerItemCategory.Ready, safe.Category);
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
        Assert.Equal(FlyerItemReasonCode.MasterDiscrepancy, Assert.Single(result.Notes).Code);
    }

    [Fact]
    public void 確認済みの商品で避ける品目でない品目が増えたら取り込めるのまま食い違いを知らせる()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat"]));

        var result = Classify(Read(label: "乳・小麦"), [master], labelCodes: ["milk", "wheat"]);

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        var discrepancy = Assert.Single(result.Discrepancies);
        Assert.Equal(Allergen("milk").Id, discrepancy.Allergen.Id);
        Assert.False(discrepancy.IsAvoided);
        var note = Assert.Single(result.Notes);
        Assert.Contains("乳", note.Message);
        Assert.Contains("取り込みを終えると「参考」の記録を足します", note.Message);
        Assert.True(note.IsAllergenRelated);
    }

    [Fact]
    public void 確認済みの含むの中に収まっていれば食い違いはない()
    {
        var master = Master(checks: ConfirmedAll(Today.AddDays(-5), ["wheat", "milk"]));

        var result = Classify(Read(label: "小麦"), [master]);

        Assert.Empty(result.Discrepancies);
        Assert.Empty(result.Notes);
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

        Assert.Equal(FlyerItemCategory.Ready, result.Category);
        Assert.Contains(result.FlyerAllergens, a => a.Id == Allergen("crab").Id);
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
