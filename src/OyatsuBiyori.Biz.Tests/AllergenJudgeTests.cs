using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class AllergenJudgeTests
{
    private static AllergenJudgement Judge(Product product, IReadOnlyList<Allergen> avoided, DateOnly? today = null)
    {
        return AllergenJudge.Judge(product, avoided, today ?? Today, RecheckDays);
    }

    [Fact]
    public void 記録がない商品は避ける品目があれば不明で入れられない()
    {
        var result = Judge(Product("さくさくクッキー"), Avoid("milk"));

        Assert.Equal(AllergenJudgementCategory.Blocked, result.Category);
        Assert.Equal(AllergenJudgementKind.Unknown, result.Kind);
    }

    [Fact]
    public void 記録がない商品は避ける品目がなくても安全にならず警告になる()
    {
        var result = Judge(Product("さくさくクッキー"), []);

        Assert.Equal(AllergenJudgementCategory.Warning, result.Category);
        Assert.Equal(AllergenJudgementKind.Unknown, result.Kind);
    }

    [Fact]
    public void 参考の商品は含む品目が空でも安全にならない()
    {
        var product = Product("ふわふわゼリー", Reference(Today.AddDays(-3)));

        var result = Judge(product, Avoid("milk"));

        Assert.Equal(AllergenJudgementCategory.Warning, result.Category);
        Assert.Equal(AllergenJudgementKind.Reference, result.Kind);
    }

    [Fact]
    public void 参考で避ける品目を含む商品は警告ではなく入れられない()
    {
        var product = Product("たまごボーロ", Reference(Today.AddDays(-3), "egg"));

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(AllergenJudgementCategory.Blocked, result.Category);
        Assert.Equal(AllergenJudgementKind.Contains, result.Kind);
        Assert.Equal([Allergen("egg").Id], result.ContainedAllergens.Select(a => a.Id));
    }

    [Fact]
    public void 商品名に探す言葉がある参考の商品は全文がなくても入れられない()
    {
        var product = Product("ヘーゼルナッツクッキー", Reference(Today.AddDays(-3), "wheat"));

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementCategory.Blocked, result.Category);
        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
        Assert.Contains(result.KeywordHits, h => h.Source == KeywordHitSource.ProductName && h.Match.AllergenId == Allergen("hazelnut").Id);
    }

    [Fact]
    public void 記録がない商品でも商品名に探す言葉があれば含む可能性ありになる()
    {
        var result = Judge(Product("ﾋﾟｰﾅｯﾂせんべい"), Nuts());

        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
    }

    [Fact]
    public void 職員が直した全文から言葉を消してもClaudeが読み取った全文にあれば入れられない()
    {
        var check = ConfirmedAll(Today.AddDays(-10), ["wheat", "milk", "soybean"],
            rawIngredients: "小麦粉、砂糖、植物油脂、ココア",
            rawIngredientsOcr: "小麦粉、砂糖、植物油脂、ヘーゼルナッツペースト、ココア");
        var product = Product("ちょこっとサンド", check);

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
        Assert.Contains(result.KeywordHits, h => h.Source == KeywordHitSource.RawIngredientsOcr && h.CheckId == check.Id);
    }

    [Fact]
    public void 職員が直した全文だけに言葉があっても入れられない()
    {
        // Claude が使えず職員が全文を手で入れた（読み取りの全文なし）。職員が読み落としを足した場合も同じ
        var check = ConfirmedAll(Today.AddDays(-10), ["wheat"], rawIngredients: "小麦粉、砂糖、ヘーゼルナッツペースト");
        check.RawIngredientsOcr = null;
        var product = Product("ちょこっとサンド", check);

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
        Assert.Contains(result.KeywordHits, h => h.Source == KeywordHitSource.RawIngredients && h.CheckId == check.Id);
        Assert.DoesNotContain(result.KeywordHits, h => h.Source == KeywordHitSource.RawIngredientsOcr);
    }

    [Fact]
    public void 商品名は照合用の正規化名ではなく商品名そのものから探す()
    {
        var product = Product("ちょこっとサンド", ConfirmedAll(Today.AddDays(-10), []));
        product.Name = "ヘーゼルナッツサンド";
        product.NormalizedName = "ちょこっとさんど";

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
        Assert.Contains(result.KeywordHits, h => h.Source == KeywordHitSource.ProductName);
    }

    [Fact]
    public void 未確認の品目は最新の確認済みだけで数え古い確認済みの行は使わない()
    {
        // 古いパッケージではヘーゼルナッツを「含まない」と確定。リニューアル後の確認ではヘーゼルナッツを避けておらず行がない
        var product = Product("ちょこっとサンド",
            ConfirmedAll(Today.AddDays(-200), []),
            Confirmed(Today.AddDays(-10), [], ["egg"]));

        var result = Judge(product, Avoid("egg", "hazelnut"));

        Assert.Equal(AllergenJudgementKind.UnconfirmedItems, result.Kind);
        Assert.Equal([Allergen("hazelnut").Id], result.UnconfirmedAllergens.Select(a => a.Id));
    }

    [Fact]
    public void ヘーゼルナッツの事例はアレルゲン表示が小麦乳大豆でも安全にならない()
    {
        // 職員がヘーゼルナッツを「含まない」と確定していても、全文に言葉があれば入れられない（SR-14）
        var check = ConfirmedAll(Today.AddDays(-10), ["wheat", "milk", "soybean"],
            rawIngredients: "砂糖、小麦粉、植物油脂、ヘーゼルナッツペースト、ココアパウダー、（一部に小麦・乳成分・大豆を含む）");
        var product = Product("ちょこっとサンド", check);

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementCategory.Blocked, result.Category);
        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
        Assert.Empty(result.ContainedAllergens);
        Assert.Contains(result.KeywordHits, h => h.Match.AllergenId == Allergen("hazelnut").Id && h.Match.MatchedText.StartsWith("ヘーゼルナッツ"));
    }

    [Theory]
    [InlineData("砂糖、ﾍｰｾﾞﾙﾅｯﾂﾍﾟｰｽﾄ、ココア")]
    [InlineData("砂糖、ヘーゼル\nナッツペースト、ココア")]
    [InlineData("砂糖、ヘーゼル\r\nナッツペースト、ココア")]
    [InlineData("砂糖、へーぜるなっつぺーすと、ココア")]
    [InlineData("砂糖、ヘ－ゼル ナッツペースト、ココア")]
    public void 探す言葉は全角半角やカタカナひらがなや改行の違いで見落とさない(string rawIngredients)
    {
        var product = Product("ちょこっとサンド", ConfirmedAll(Today.AddDays(-10), ["wheat"], rawIngredients));

        var result = Judge(product, Avoid("hazelnut"));

        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
    }

    [Fact]
    public void 保存済みの正規化した言葉が古くても言葉を正規化し直して探す()
    {
        var hazelnut = new Allergen { Name = "ヘーゼルナッツ", Category = AllergenCategory.FacilityAdded };
        hazelnut.Keywords.Add(new AllergenKeyword { AllergenId = hazelnut.Id, Keyword = "ヘーゼルナッツ", NormalizedKeyword = "ヘーゼルナッツ" });
        var product = Product("ちょこっとサンド", ConfirmedAll(Today.AddDays(-10), ["wheat"], "砂糖、ﾍｰｾﾞﾙﾅｯﾂﾍﾟｰｽﾄ"));

        var result = Judge(product, [hazelnut]);

        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
    }

    [Fact]
    public void 確認済みの全文に言葉があればそのあと全文のない参考が最新になっても入れられない()
    {
        var product = Product("ちょこっとサンド",
            ConfirmedAll(Today.AddDays(-30), ["wheat"], "砂糖、ヘーゼルナッツペースト"),
            Reference(Today.AddDays(-2), "wheat"));

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementCategory.Blocked, result.Category);
        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
    }

    [Theory]
    [InlineData(90, AllergenJudgementKind.Safe)]
    [InlineData(91, AllergenJudgementKind.RecheckNeeded)]
    public void 再確認期間は90日目まで期限内で91日目から要再確認(int daysAgo, AllergenJudgementKind expected)
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-daysAgo), []));

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(expected, result.Kind);
        Assert.Equal(expected == AllergenJudgementKind.Safe ? AllergenJudgementCategory.Safe : AllergenJudgementCategory.Warning, result.Category);
    }

    [Fact]
    public void 確認日は日本時間の日付で数える()
    {
        // 日本時間 2026-08-05 00:30（UTC では 08-04 15:30）。今日（11-03）から 90日前で期限内
        var check = ConfirmedAll(Today, []);
        check.CheckedAt = new DateTime(2026, 8, 4, 15, 30, 0, DateTimeKind.Utc);
        var product = Product("のりしおポテト", check);

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(new DateOnly(2026, 8, 5), result.LastConfirmedDate);
        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
    }

    [Fact]
    public void 最新の記録が参考ならそれより前の確認済みより参考が優先される()
    {
        var product = Product("のりしおポテト",
            ConfirmedAll(Today.AddDays(-20), []),
            Reference(Today.AddDays(-1)));

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(AllergenJudgementKind.Reference, result.Kind);
    }

    [Fact]
    public void 同じ日時の確認済みと参考が並んだら参考を最新として扱う()
    {
        var confirmed = ConfirmedAll(Today.AddDays(-1), []);
        var reference = Reference(Today.AddDays(-1));
        reference.CheckedAt = confirmed.CheckedAt;
        // 並び順に左右されないことを確かめるため、参考を先に渡す
        var product = Product("のりしおポテト", reference, confirmed);

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(AllergenJudgementKind.Reference, result.Kind);
        Assert.Equal(reference.Id, result.LatestCheckId);
    }

    [Fact]
    public void 避ける品目に表示が確実でない品目があれば安全でもメーカーへの確認が必要を付ける()
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-10), []));

        var result = Judge(product, Avoid("egg", "almond", "cashew"));

        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
        Assert.True(result.NeedsManufacturerCheck);
        Assert.Equal([Allergen("cashew").Id, Allergen("almond").Id], result.ManufacturerCheckAllergens.Select(a => a.Id));
    }

    [Fact]
    public void 避ける品目に表示が確実でない品目がなければメーカーへの確認は付けない()
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-10), []));

        var result = Judge(product, Avoid("egg", "hazelnut"));

        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
        Assert.False(result.NeedsManufacturerCheck);
    }

    [Fact]
    public void 参考で乳と卵のあと次のチラシで乳だけになっても卵は含むのまま()
    {
        var product = Product("ミルクたまごパン",
            Reference(Today.AddDays(-14), "milk", "egg"),
            Reference(Today.AddDays(-1), "milk"));

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(AllergenJudgementKind.Contains, result.Kind);
    }

    [Fact]
    public void 確認済みのあとに含む品目のない参考を足しても確認済みの含むは残る()
    {
        var product = Product("ミルクたまごパン",
            ConfirmedAll(Today.AddDays(-20), ["egg"]),
            Reference(Today.AddDays(-1)));

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(AllergenJudgementKind.Contains, result.Kind);
    }

    [Fact]
    public void 新しい確認済みで含まないにした品目はそれより前の参考の含むに引きずられない()
    {
        var product = Product("ミルクパン",
            Reference(Today.AddDays(-20), "egg"),
            ConfirmedAll(Today.AddDays(-5), ["milk"]));

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
    }

    [Fact]
    public void 避ける品目に新しい品目を足したときその品目を確定していない商品は安全にならない()
    {
        // 確認したときは卵だけを避けていた（ごまは確定していない）
        var product = Product("のりしおポテト", Confirmed(Today.AddDays(-10), [], ["egg"]));

        var result = Judge(product, Avoid("egg", "sesame"));

        Assert.Equal(AllergenJudgementCategory.Warning, result.Category);
        Assert.Equal(AllergenJudgementKind.UnconfirmedItems, result.Kind);
        Assert.Equal([Allergen("sesame").Id], result.UnconfirmedAllergens.Select(a => a.Id));
    }

    [Fact]
    public void 参考の記録の含まないの行は未確認の品目を減らさない()
    {
        var reference = Reference(Today.AddDays(-20));
        reference.Items.Add(new AllergenCheckItem { AllergenCheckId = reference.Id, AllergenId = Allergen("sesame").Id, Contains = false });
        var product = Product("のりしおポテト", reference, Confirmed(Today.AddDays(-10), [], ["egg"]));

        var result = Judge(product, Avoid("egg", "sesame"));

        Assert.Equal(AllergenJudgementKind.UnconfirmedItems, result.Kind);
    }

    [Fact]
    public void 含むと探す言葉が両方あれば含むを優先し理由は両方返す()
    {
        var product = Product("くるみクッキー", Reference(Today.AddDays(-1), "walnut"));

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementKind.Contains, result.Kind);
        Assert.NotEmpty(result.ContainedAllergens);
        Assert.NotEmpty(result.KeywordHits);
        Assert.Empty(result.ManufacturerCheckAllergens);
    }

    [Fact]
    public void 避ける品目でない品目を含んでも判定には影響しない()
    {
        var product = Product("ミルクパン", ConfirmedAll(Today.AddDays(-10), ["milk", "wheat"]));

        var result = Judge(product, Avoid("egg"));

        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
        Assert.Empty(result.ContainedAllergens);
    }

    [Fact]
    public void すべて確定していて期限内で含まなければ安全()
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-10), ["wheat"], "じゃがいも、植物油、食塩、青のり"));

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementCategory.Safe, result.Category);
        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
        Assert.Empty(result.KeywordHits);
    }

    [Fact]
    public void 避ける品目がなければ参考は警告でメーカーへの確認は付けない()
    {
        var result = Judge(Product("ふわふわゼリー", Reference(Today.AddDays(-1))), []);

        Assert.Equal(AllergenJudgementCategory.Warning, result.Category);
        Assert.Equal(AllergenJudgementKind.Reference, result.Kind);
        Assert.False(result.NeedsManufacturerCheck);
    }

    [Fact]
    public void 最新が参考でも確認済みで未確定の避ける品目を理由に返す()
    {
        var product = Product("のりしおポテト", Confirmed(Today.AddDays(-20), [], ["egg"]), Reference(Today.AddDays(-1)));

        var result = Judge(product, Avoid("egg", "sesame"));

        Assert.Equal(AllergenJudgementKind.Reference, result.Kind);
        Assert.Equal([Allergen("sesame").Id], result.UnconfirmedAllergens.Select(a => a.Id));
    }

    [Theory]
    [InlineData("ヘ-ゼルナッツクッキー")]
    [InlineData("ﾍｰｾﾞﾙ ｸｯｷｰ")]
    public void 商品名の書き違いや半角でも探す言葉に当たる(string name)
    {
        var result = Judge(Product(name, Reference(Today.AddDays(-1))), Nuts());

        Assert.Equal(AllergenJudgementKind.MayContain, result.Kind);
    }

    [Fact]
    public void 避ける品目が重なって渡されても結果は変わらない()
    {
        var product = Product("のりしおポテト", Confirmed(Today.AddDays(-10), [], ["egg"]));

        var once = Judge(product, Avoid("egg", "sesame"));
        var twice = Judge(product, Avoid("egg", "sesame", "egg", "sesame"));

        Assert.Equal(once.Kind, twice.Kind);
        Assert.Equal(once.UnconfirmedAllergens, twice.UnconfirmedAllergens);
    }

    [Fact]
    public void 施設の追加品目の探す言葉が読み込まれていなければ黙って判定せず止める()
    {
        var hazelnut = new Allergen { Name = "ヘーゼルナッツ", Category = AllergenCategory.FacilityAdded };

        Assert.Throws<InvalidOperationException>(() => Judge(Product("ちょこっとサンド", Reference(Today.AddDays(-1))), [hazelnut]));
    }

    [Fact]
    public void 初期データで言葉を持つ29品目の探す言葉が読み込まれていなければ止める()
    {
        var seedPeanut = Allergen("peanut");
        var peanut = new Allergen { Id = seedPeanut.Id, Name = seedPeanut.Name, Category = seedPeanut.Category };

        Assert.Throws<InvalidOperationException>(() => Judge(Product("ちょこっとサンド", Reference(Today.AddDays(-1))), [peanut]));
    }

    [Fact]
    public void 避ける品目がなければ商品名に言葉があっても含む可能性ありにしない()
    {
        var result = Judge(Product("ヘーゼルナッツクッキー", Reference(Today.AddDays(-1))), []);

        Assert.Equal(AllergenJudgementKind.Reference, result.Kind);
        Assert.Empty(result.KeywordHits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 再確認期間が1日未満なら止める(int recheckDays)
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-1), []));

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => AllergenJudge.Judge(product, Avoid("egg"), Today, recheckDays));
    }

    [Fact]
    public void 同一工場の注意書きは探す言葉で調べない()
    {
        // 注意書きの扱いは職員に委ねる（SR-11）。全文に入っていれば全文で当たる
        var check = ConfirmedAll(Today.AddDays(-10), [], "じゃがいも、植物油、食塩");
        check.ContaminationNote = "本品製造工場では落花生を含む製品を製造しています";

        var result = Judge(Product("のりしおポテト", check), Nuts());

        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
    }

    [Fact]
    public void 古い確認済みの全文は有効な記録でないので探さない()
    {
        // 古いパッケージにはヘーゼルナッツがあったが、リニューアル後の確認済みでなくなった
        var product = Product("ちょこっとサンド",
            ConfirmedAll(Today.AddDays(-200), ["wheat"], "砂糖、ヘーゼルナッツペースト"),
            ConfirmedAll(Today.AddDays(-10), ["wheat"], "砂糖、ココアパウダー"));

        var result = Judge(product, Nuts());

        Assert.Equal(AllergenJudgementKind.Safe, result.Kind);
    }
}
