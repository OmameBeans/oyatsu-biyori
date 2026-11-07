using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.MasterData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class FlyerAllergenPlannerTests
{
    private static readonly DateTime ImportedAtUtc = NoonUtc(Today);

    private static FlyerLabelParseResult Parse(string? labelText) => FlyerAllergenPlanner.ParseLabel(labelText, AllAllergens);

    private static string[] Ids(params string[] codes) => codes.Select(c => Allergen(c).Id).ToArray();

    /// <summary>表記を読み取り、そのまま（職員が直さずに）計画を立てる</summary>
    private static FlyerAllergenPlan Plan(string? labelText, string? description, string[] avoided, params AllergenCheck[] existing)
    {
        var parsed = Parse(labelText);
        var input = new FlyerAllergenInput(parsed.Allergens.Select(a => a.Id).ToList(), labelText, description, parsed.UnmappedTerms);
        return FlyerAllergenPlanner.Plan(input, AllAllergens, Ids(avoided), existing);
    }

    private static AllergenCheck ApplyTo(Product product, FlyerAllergenPlan plan)
    {
        var check = FlyerAllergenPlanner.CreateReferenceCheck(product.Id, plan, ImportedAtUtc, "test-user", "test-user");
        product.AllergenChecks.Add(check);
        return check;
    }

    [Theory]
    [InlineData("乳・小麦", new[] { "wheat", "milk" })]
    [InlineData("卵、乳成分、小麦", new[] { "wheat", "egg", "milk" })]
    [InlineData("（一部に乳成分・小麦を含む）", new[] { "wheat", "milk" })]
    [InlineData("落花生（ピーナッツ）", new[] { "peanut" })]
    [InlineData("ピーナツ", new[] { "peanut" })]
    [InlineData("牛乳", new[] { "milk" })]
    [InlineData("鶏卵", new[] { "egg" })]
    [InlineData("ｱｰﾓﾝﾄﾞ", new[] { "almond" })]
    [InlineData("ヘーゼルナッツ", new[] { "hazelnut" })]
    public void 表記をマスタの品目に対応づける(string labelText, string[] expected)
    {
        var result = Parse(labelText);

        Assert.Equal(Ids(expected).ToHashSet(), result.Allergens.Select(a => a.Id).ToHashSet());
        Assert.Empty(result.UnmappedTerms);
    }

    [Theory]
    [InlineData("麦", new[] { "wheat" })]
    [InlineData("え", new[] { "shrimp" })]
    [InlineData("か", new[] { "crab" })]
    [InlineData("落", new[] { "peanut" })]
    [InlineData("そ", new[] { "buckwheat" })]
    [InlineData("く", new[] { "walnut" })]
    [InlineData("カ", new[] { "cashew" })]
    [InlineData("ｶ", new[] { "cashew" })]
    [InlineData("乳・カ", new[] { "milk", "cashew" })]
    [InlineData("か・カ", new[] { "crab", "cashew" })]
    [InlineData("乳・麦", new[] { "wheat", "milk" })]
    [InlineData("（麦・え）", new[] { "shrimp", "wheat" })]
    [InlineData("卵・乳・麦", new[] { "wheat", "egg", "milk" })]
    [InlineData("一部に麦を含む", new[] { "wheat" })]
    [InlineData("乳・麦・小麦", new[] { "wheat", "milk" })]
    public void D_96_チラシの略記をマスタの品目に当てる(string labelText, string[] expected)
    {
        var result = Parse(labelText);

        Assert.Equal(Ids(expected).ToHashSet(), result.Allergens.Select(a => a.Id).ToHashSet());
        Assert.Empty(result.UnmappedTerms);
    }

    [Theory]
    [InlineData("くっきー")]
    [InlineData("えびせん")]
    [InlineData("かぼちゃ")]
    [InlineData("そら豆")]
    [InlineData("麦芽")]
    [InlineData("落雁")]
    [InlineData("カカオ")]
    public void D_96_略記は語全体が一致したときだけ当て_部分一致では当てない(string term)
    {
        var (allergens, isFullyMapped) = FlyerAllergenPlanner.MatchAbbreviations(term, AllAllergens);

        Assert.Empty(allergens);
        Assert.False(isFullyMapped);
    }

    [Theory]
    [InlineData("エ")]
    [InlineData("ソ")]
    [InlineData("ク")]
    [InlineData("ｴ")]
    public void D_100_エ_ソ_クのカタカナは_ひらがなの略記に当てず品目不明として残す(string labelText)
    {
        // マスタの略記はひらがなで書かれているので、カタカナでは当てない（FR-IMPORT-32。職員に選んでもらう。確認する側）
        var result = Parse(labelText);
        var (allergens, isFullyMapped) = FlyerAllergenPlanner.MatchAbbreviations(labelText, AllAllergens);

        Assert.Empty(result.Allergens);
        Assert.Equal([labelText], result.UnmappedTerms);
        Assert.Empty(allergens);
        Assert.False(isFullyMapped);
    }

    [Theory]
    [InlineData("か", "crab", "cashew")]
    [InlineData("カ", "cashew", "crab")]
    [InlineData("ｶ", "cashew", "crab")]
    public void D_100_ひらがなのかはかに_カタカナのカと半角のｶはカシューナッツにだけ当たる(string term, string expected, string notExpected)
    {
        var parsed = Parse(term);
        var (allergens, isFullyMapped) = FlyerAllergenPlanner.MatchAbbreviations(term, AllAllergens);

        Assert.Equal([Allergen(expected).Id], parsed.Allergens.Select(a => a.Id));
        Assert.DoesNotContain(parsed.Allergens, a => a.Id == Allergen(notExpected).Id);
        Assert.Equal([Allergen(expected).Id], allergens.Select(a => a.Id));
        Assert.True(isFullyMapped);
    }

    [Theory]
    [InlineData("エ")]
    [InlineData("ソ")]
    [InlineData("ク")]
    public void D_100_Claude_の未対応の語でも_エ_ソ_クのカタカナは略記に当てない(string term)
    {
        var (allergens, isFullyMapped) = FlyerAllergenPlanner.MatchAbbreviations(term, AllAllergens);

        Assert.Empty(allergens);
        Assert.False(isFullyMapped);
    }

    [Fact]
    public void D_100_略記で当たった語と品目を返す()
    {
        var parsed = Parse("乳・麦・カ");
        var matched = FlyerAllergenPlanner.MatchAbbreviations("麦・カ", AllAllergens);

        Assert.Equal(
            [("カ", Allergen("cashew").Id), ("麦", Allergen("wheat").Id)],
            parsed.AbbreviationHits.Select(h => (h.Term, h.Allergen.Id)).OrderBy(h => h.Item1, StringComparer.Ordinal));
        Assert.Equal(
            [("カ", Allergen("cashew").Id), ("麦", Allergen("wheat").Id)],
            matched.Hits.Select(h => (h.Term, h.Allergen.Id)).OrderBy(h => h.Item1, StringComparer.Ordinal));
    }

    [Fact]
    public void D_96_Claude_の未対応の語の_なし_は当たらない部分として残す()
    {
        var (allergens, isFullyMapped) = FlyerAllergenPlanner.MatchAbbreviations("麦・なし", AllAllergens);

        Assert.Equal([Allergen("wheat").Id], allergens.Select(a => a.Id));
        Assert.False(isFullyMapped);
    }

    [Fact]
    public void D_96_略記に当たらない語は_これまでどおり対応づけできない語として残す()
    {
        var walnut = Parse("くっきー");
        var shrimp = Parse("えびせん");

        Assert.Empty(walnut.Allergens);
        Assert.Equal(["くっきー"], walnut.UnmappedTerms);
        // 「えびせん」は品目名「えび」を含むので、品目名の照合（これまでどおり）でえびに当たる
        Assert.Equal([Allergen("shrimp").Id], shrimp.Allergens.Select(a => a.Id));
    }

    [Fact]
    public void D_96_略記は探す言葉に入れない()
    {
        // 探す言葉の正規化（かなをそろえる）でも比べる（「カ」→「か」が探す言葉に入っていないことも確かめる）
        var abbreviations = MasterSeed.LabelAbbreviations().Select(a => TextNormalizer.ForKeywordSearch(a.Abbreviation)).ToHashSet();

        Assert.DoesNotContain(MasterSeed.Keywords(), k => abbreviations.Contains(k.NormalizedKeyword));
        // 原材料の全文の「くるみ」以外の「く」（「くず粉」など）で、くるみが当たらない
        Assert.Empty(AllergenKeywordFinder.Find("くず粉、えんどう豆、そら豆", [Allergen("walnut"), Allergen("shrimp"), Allergen("buckwheat")]));
    }

    [Fact]
    public void D_96_Claude_の未対応の語は_すべての部分が略記に当たったときだけ外してよい()
    {
        var full = FlyerAllergenPlanner.MatchAbbreviations("麦・え", AllAllergens);
        var partial = FlyerAllergenPlanner.MatchAbbreviations("麦・たまご", AllAllergens);

        Assert.True(full.IsFullyMapped);
        Assert.Equal(Ids("shrimp", "wheat").ToHashSet(), full.Allergens.Select(a => a.Id).ToHashSet());
        Assert.False(partial.IsFullyMapped);
        Assert.Equal([Allergen("wheat").Id], partial.Allergens.Select(a => a.Id));
    }

    [Fact]
    public void 一つの語が複数の品目に当たればすべて採る()
    {
        var result = Parse("プラリネ");

        Assert.Contains(result.Allergens, a => a.Id == Allergen("almond").Id);
        Assert.Contains(result.Allergens, a => a.Id == Allergen("hazelnut").Id);
    }

    [Fact]
    public void 対応づけできない語は捨てずに返す()
    {
        var result = Parse("たまご・小麦");

        Assert.Equal([Allergen("wheat").Id], result.Allergens.Select(a => a.Id));
        Assert.Equal(["たまご"], result.UnmappedTerms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("なし")]
    [InlineData("（なし）")]
    [InlineData("－")]
    [InlineData("表記なし")]
    public void なしや記号だけの表記は品目も対応づけできない語もない(string? labelText)
    {
        var result = Parse(labelText);

        Assert.Empty(result.Allergens);
        Assert.Empty(result.UnmappedTerms);
    }

    [Fact]
    public void 記録がない商品は表記なしでも参考の記録を追加しどの品目も含まないにしない()
    {
        var product = Product("ふわふわゼリー");

        var plan = Plan(null, "ぷるんとしたゼリー", ["milk"]);
        var check = ApplyTo(product, plan);

        Assert.Equal(FlyerReferenceDecision.Add, plan.Decision);
        Assert.True(plan.ShouldAddReference());
        Assert.Equal("表記なし", check.SourceNote);
        Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        Assert.Equal(AllergenCheckSource.Flyer, check.Source);
        Assert.Equal(ImportedAtUtc, check.CheckedAt);
        Assert.Empty(check.Items);
        Assert.Equal(AllergenJudgementKind.Reference, AllergenJudge.Judge(product, Avoid("milk"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void 記録がない商品で表記の品目を含むとして記録し推奨品目も含むにする()
    {
        var product = Product("ミルクプリン");

        var check = ApplyTo(product, Plan("乳・ゼラチン", null, ["egg"]));

        Assert.Equal("表記：乳・ゼラチン", check.SourceNote);
        Assert.Equal(Ids("milk", "gelatin").ToHashSet(), check.Items.Select(i => i.AllergenId).ToHashSet());
        Assert.All(check.Items, i => Assert.True(i.Contains));
    }

    [Fact]
    public void 説明文で避ける品目の探す言葉が当たればチラシの品目に入れて言葉を残す()
    {
        var product = Product("ちょこっとサンド");

        var plan = Plan("小麦・乳", "香ばしいヘーゼルナッツ入りのチョコクリーム", ["hazelnut"]);
        var check = ApplyTo(product, plan);

        Assert.Contains(plan.FlyerAllergens, a => a.Id == Allergen("hazelnut").Id);
        Assert.Contains("商品名・説明文で当たった言葉：ヘーゼルナッツ", check.SourceNote);
        Assert.Equal(AllergenJudgementCategory.Blocked, AllergenJudge.Judge(product, Avoid("hazelnut"), Today, RecheckDays).Category);
    }

    [Fact]
    public void 説明文の言葉は避ける品目でなければチラシの品目に入れない()
    {
        var plan = Plan("小麦・乳", "香ばしいヘーゼルナッツ入りのチョコクリーム", ["egg"]);

        Assert.DoesNotContain(plan.FlyerAllergens, a => a.Id == Allergen("hazelnut").Id);
        Assert.Empty(plan.DescriptionHits);
    }

    [Fact]
    public void 確認済みの商品にチラシを取り込んでも表記が一致すれば参考は追加されない()
    {
        // 確認済みは期限切れでも同じ扱い
        var plan = Plan("乳・小麦", null, ["egg"], ConfirmedAll(Today.AddDays(-200), ["milk", "wheat"]));

        Assert.Equal(FlyerReferenceDecision.None, plan.Decision);
        Assert.False(plan.ShouldAddReference());
        Assert.Empty(plan.Discrepancies);
    }

    [Fact]
    public void 確認済みの商品で表記なしなら参考は追加されない()
    {
        var plan = Plan(null, null, ["egg"], ConfirmedAll(Today.AddDays(-10), ["milk"]));

        Assert.Equal(FlyerReferenceDecision.None, plan.Decision);
    }

    [Fact]
    public void チラシの表記に確認済みでは含まないの避ける品目があれば参考を必ず追加して入れられなくなる()
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-10), ["wheat"]));
        Assert.Equal(AllergenJudgementKind.Safe, AllergenJudge.Judge(product, Avoid("milk"), Today, RecheckDays).Kind);

        var plan = Plan("乳・小麦", null, ["milk"], [.. product.AllergenChecks]);

        Assert.Equal(FlyerReferenceDecision.AddForDiscrepancy, plan.Decision);
        Assert.True(plan.ShouldAddReference());
        var discrepancy = Assert.Single(plan.Discrepancies);
        Assert.Equal(Allergen("milk").Id, discrepancy.Allergen.Id);
        Assert.True(discrepancy.IsAvoided);

        ApplyTo(product, plan);
        var result = AllergenJudge.Judge(product, Avoid("milk"), Today, RecheckDays);
        Assert.Equal(AllergenJudgementCategory.Blocked, result.Category);
        Assert.Equal(AllergenJudgementKind.Contains, result.Kind);
    }

    [Fact]
    public void 確認済みで行のない未確定の避ける品目がチラシにあっても食い違いにする()
    {
        // 確認したときは乳を避けていなかった（乳の行がない）
        var product = Product("のりしおポテト", Confirmed(Today.AddDays(-10), ["wheat"], ["egg"]));

        var plan = Plan("乳・小麦", null, ["milk", "egg"], [.. product.AllergenChecks]);

        Assert.Equal(FlyerReferenceDecision.AddForDiscrepancy, plan.Decision);
        ApplyTo(product, plan);
        Assert.Equal(AllergenJudgementKind.Contains, AllergenJudge.Judge(product, Avoid("milk", "egg"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void D_100_避ける品目でない食い違いだけでも_職員に選ばせず必ず追加し_判定は要確認になる()
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-10), ["wheat"]));

        var plan = Plan("乳・小麦", null, ["egg"], [.. product.AllergenChecks]);

        Assert.Equal(FlyerReferenceDecision.AddForDiscrepancy, plan.Decision);
        Assert.False(Assert.Single(plan.Discrepancies).IsAvoided);
        Assert.True(plan.ShouldAddReference());
        ApplyTo(product, plan);
        Assert.Equal(AllergenJudgementCategory.Warning, AllergenJudge.Judge(product, Avoid("egg"), Today, RecheckDays).Category);
    }

    [Fact]
    public void 説明文だけで当たった食い違いは根拠を示す()
    {
        var plan = Plan("小麦", "ヘーゼルナッツ入り", ["hazelnut"], ConfirmedAll(Today.AddDays(-10), ["wheat"]));

        var discrepancy = Assert.Single(plan.Discrepancies);
        Assert.True(discrepancy.OnlyFromDescription);
        Assert.Equal(FlyerReferenceDecision.AddForDiscrepancy, plan.Decision);
    }

    [Fact]
    public void 確認済みのあとの参考ですでに含むになっていれば食い違いにしない()
    {
        var plan = Plan("乳", null, ["milk"],
            ConfirmedAll(Today.AddDays(-30), []),
            Reference(Today.AddDays(-7), "milk"));

        Assert.Equal(FlyerReferenceDecision.None, plan.Decision);
    }

    [Fact]
    public void 参考で乳と卵のあと次のチラシで乳だけでも追加せず卵は含むのまま()
    {
        var product = Product("ミルクたまごパン", Reference(Today.AddDays(-7), "milk", "egg"));

        var plan = Plan("乳", null, ["egg"], [.. product.AllergenChecks]);

        Assert.Equal(FlyerReferenceDecision.None, plan.Decision);
        Assert.Equal(AllergenJudgementKind.Contains, AllergenJudge.Judge(product, Avoid("egg"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void 参考だけの商品でチラシの品目が増えたら追加する()
    {
        var product = Product("ミルクたまごパン", Reference(Today.AddDays(-7), "milk"));

        var plan = Plan("乳・卵", null, ["egg"], [.. product.AllergenChecks]);
        var check = ApplyTo(product, plan);

        Assert.Equal(FlyerReferenceDecision.Add, plan.Decision);
        Assert.Equal(Ids("egg", "milk").ToHashSet(), check.Items.Select(i => i.AllergenId).ToHashSet());
        Assert.Equal(AllergenJudgementKind.Contains, AllergenJudge.Judge(product, Avoid("egg"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void 対応づけできない語が残っていれば確定させず記録も作らない()
    {
        var plan = Plan("たまご・小麦", null, ["egg"]);

        Assert.True(plan.HasUnresolvedLabelTerms);
        Assert.False(plan.CanConfirm);
        Assert.Equal(["たまご"], plan.UnresolvedLabelTerms);
        Assert.Throws<InvalidOperationException>(() => FlyerAllergenPlanner.CreateReferenceCheck("product-1", plan, ImportedAtUtc, "test-user", "test-user"));
    }

    [Fact]
    public void 参考だけで品目が増えないときも対応づけできない語が残れば確定させない()
    {
        var plan = Plan("たまご・小麦", null, ["egg"], Reference(Today.AddDays(-7), "wheat"));

        Assert.Equal(FlyerReferenceDecision.None, plan.Decision);
        Assert.False(plan.CanConfirm);
    }

    [Fact]
    public void 職員が卵に対応づければ卵を含むとして記録し表記の原文を残す()
    {
        var product = Product("たまごボーロ");
        var input = new FlyerAllergenInput(Ids("egg", "wheat"), "たまご・小麦", null, []);

        var plan = FlyerAllergenPlanner.Plan(input, AllAllergens, Ids("egg"), []);
        var check = ApplyTo(product, plan);

        Assert.True(plan.CanConfirm);
        Assert.Equal("表記：たまご・小麦", check.SourceNote);
        Assert.Equal(AllergenJudgementKind.Contains, AllergenJudge.Judge(product, Avoid("egg"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void 職員がチラシと見比べて外した品目はチラシの品目に入らない()
    {
        // 読み取りは「乳・小麦」だったが、職員がチラシと見比べて乳を外した（SR-19。外せるのは職員だけ）
        var input = new FlyerAllergenInput(Ids("wheat"), "乳・小麦", null, []);

        var plan = FlyerAllergenPlanner.Plan(input, AllAllergens, Ids("milk"), []);

        Assert.Equal(Ids("wheat"), plan.FlyerAllergens.Select(a => a.Id));
    }

    [Fact]
    public void 避ける品目と避けない品目の食い違いが混ざれば必ず追加し両方を含むにする()
    {
        var product = Product("のりしおポテト", ConfirmedAll(Today.AddDays(-10), []));

        var plan = Plan("乳・小麦", null, ["milk"], [.. product.AllergenChecks]);
        var check = ApplyTo(product, plan);

        Assert.Equal(FlyerReferenceDecision.AddForDiscrepancy, plan.Decision);
        Assert.True(plan.ShouldAddReference());
        Assert.Equal(Ids("wheat", "milk").ToHashSet(), check.Items.Select(i => i.AllergenId).ToHashSet());
    }

    [Theory]
    [InlineData("一部に乳成分を含む", new[] { "milk" })]
    [InlineData("小麦・乳（ヘーゼルナッツペースト使用）", new[] { "wheat", "milk", "hazelnut" })]
    [InlineData("乳なし", new[] { "milk" })]
    [InlineData("卵不使用", new[] { "egg" })]
    public void 品目名を含む語は多めに対応づける(string labelText, string[] expected)
    {
        // 「乳なし」「卵不使用」も「含む」に倒す（確認する側。試用で見直す。U-56）
        var result = Parse(labelText);

        Assert.Equal(Ids(expected).ToHashSet(), result.Allergens.Select(a => a.Id).ToHashSet());
    }

    [Fact]
    public void 確認日時がUTCでなければ記録を作らない()
    {
        var plan = Plan("乳", null, ["milk"]);

        Assert.Throws<ArgumentException>(() => FlyerAllergenPlanner.CreateReferenceCheck("product-1", plan, DateTime.SpecifyKind(ImportedAtUtc, DateTimeKind.Unspecified), "test-user", null));
    }

    [Fact]
    public void 記録には含まないの行を作らない()
    {
        var plans = new[]
        {
            Plan("乳・小麦", "ヘーゼルナッツ入り", ["hazelnut", "egg"]),
            Plan(null, null, ["egg"]),
            Plan("乳", null, ["milk"], ConfirmedAll(Today.AddDays(-10), [])),
        };

        foreach (var plan in plans)
        {
            var check = FlyerAllergenPlanner.CreateReferenceCheck("product-1", plan, ImportedAtUtc, "test-user", null);
            Assert.All(check.Items, i => Assert.True(i.Contains));
            Assert.Equal(AllergenCheckStatus.Reference, check.Status);
        }
    }

    [Fact]
    public void 確認者がなければ記録を作らない()
    {
        var plan = Plan("乳", null, ["milk"]);

        Assert.ThrowsAny<ArgumentException>(() => FlyerAllergenPlanner.CreateReferenceCheck("product-1", plan, ImportedAtUtc, " ", null));
    }

    [Fact]
    public void マスタにない品目は受け付けない()
    {
        var input = new FlyerAllergenInput(["unknown-id"], "乳", null, []);

        Assert.Throws<ArgumentException>(() => FlyerAllergenPlanner.Plan(input, AllAllergens, [], []));
    }
}
