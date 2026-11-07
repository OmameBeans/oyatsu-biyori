using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.MasterData;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Server.DemoData;

/// <summary>
/// ダミーデータの投入（TD-02。HK-DEMO-04、HK-CC-05、U-39）。商品・メーカーの名前はすべて架空。
/// 日付は投入する日からの相対値（確認済みは20日前、要再確認は100日前、重複させる献立は2週前など）。
/// 本番では動かさない（DemoDataGuard で止める）。
/// </summary>
public sealed class DemoDataSeeder(OyatsuBiyoriDbContext context, TimeProvider timeProvider, ILogger<DemoDataSeeder> logger)
{
    private readonly OyatsuBiyoriDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<DemoDataSeeder> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private List<Allergen> _allergens = [];
    private DateOnly _today;
    private string _checker = "staff";
    private string? _userId;

    /// <summary>
    /// 投入する（商品がすでにあれば何もしない）。
    /// </summary>
    /// <param name="includeE2EDraft">E2E 用に、掲載品の写しを持つ下書き（再来週）と、いちごゼリーを置いた下書き（3週先。献立一覧の注意を曜日に関係なく出すため）も作る</param>
    public async Task SeedAsync(bool includeE2EDraft)
    {
        if (await _context.Products.AnyAsync())
        {
            _logger.LogInformation("商品がすでにあるため、ダミーデータは入れません");
            return;
        }

        _today = JapanDate.Today(_timeProvider);
        _allergens = await _context.Allergens.Include(a => a.Keywords).OrderBy(a => a.SortOrder).ToListAsync();
        var user = await _context.Users.OrderBy(u => u.Id).FirstOrDefaultAsync();
        _checker = user?.UserName ?? "staff";
        _userId = user?.Id;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // 条件設定：50人・既定の週の予算 20,000円・ナッツ類（すべて）を避ける
        var setting = await _context.Settings.FirstAsync();
        setting.DefaultChildrenCount = 50;
        setting.DefaultWeeklyBudget = 20000;
        setting.DuplicateRuleNote = "味違いの商品は、同じ週に続けて出さない";
        setting.UpdateDateTime = now;

        var nutGroup = await _context.AllergenGroups.Include(g => g.Members).FirstAsync(g => g.Id == SeedIds.For("group:nuts"));
        foreach (var member in nutGroup.Members)
        {
            _context.AvoidedAllergens.Add(new AvoidedAllergen { AllergenId = member.AllergenId, AddedAt = now, AddedBy = _checker, UpdateDateTime = now, UpdateUserId = _userId });
        }

        var products = CreateProducts();
        _context.Products.AddRange(products.Values);

        var weekStart = JapanDate.WeekStart(_today);
        _context.Menus.Add(ConfirmedMenu(weekStart.AddDays(-14), products, [
            ["yasai-senbei", "ichigo-jelly"],
            ["milk-biscuit", "yogurt-jelly"],
            ["yasai-senbei", "mikan-jelly-old"],
            ["kokutou-fugashi", "ichigo-jelly"],
            ["umi-senbei", "mushipan"],
        ]));
        _context.Menus.Add(ConfirmedMenu(weekStart.AddDays(-7), products, [
            ["usushio", "yogurt-jelly"],
            ["milk-biscuit", "ichigo-jelly"],
            ["okome-snack", "mushipan"],
            ["sakusaku-cookie", "yogurt-jelly"],
            ["umi-senbei", "kokutou-fugashi"],
        ]));

        // 今週は確定済み。いちごゼリーは「参考」のときに確認したため、今の判定（要再確認）と違い「確認し直し」になる（FR-MENU-34a）
        // いちごゼリーは金曜に置く（平日なら「今日以降の献立で使う、パッケージで確認する商品」に入る）
        var thisWeek = ConfirmedMenu(weekStart, products, [
            ["okome-snack", "yogurt-jelly"],
            ["milk-biscuit", "sakusaku-cookie"],
            ["usushio", "mushipan"],
            ["kokutou-fugashi", "yogurt-jelly"],
            ["umi-senbei", "ichigo-jelly"],
        ]);
        foreach (var item in thisWeek.Days.SelectMany(d => d.Items).Where(i => i.ProductId == products["ichigo-jelly"].Id))
        {
            item.AcknowledgedAt = now.AddDays(-3);
            item.AcknowledgedJudgement = AllergenJudgementKind.Reference;
            item.AcknowledgedJudgementFingerprint = "seed";
        }

        _context.Menus.Add(thisWeek);

        if (includeE2EDraft)
        {
            _context.Menus.Add(DraftWithListing(weekStart.AddDays(14), products));

            // 3週先の下書きの月曜に、いちごゼリー（🟡）を置く。曜日に関係なく（土日でも）
            // 「今日以降の献立で使う、パッケージで確認する商品」が1件以上になり、献立一覧の注意が出る（SC-10 の E2E）
            var later = ConfirmedMenu(weekStart.AddDays(21), products, [["ichigo-jelly"]]);
            later.Status = MenuStatus.Draft;
            later.ConfirmedAt = null;
            later.ConfirmedBy = null;
            _context.Menus.Add(later);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("ダミーデータを入れました（商品 {Count}件）", products.Count);
    }

    /// <summary>商品（キーは投入の中だけで使う名前）</summary>
    private Dictionary<string, Product> CreateProducts()
    {
        var p = new Dictionary<string, Product>(StringComparer.Ordinal);

        // チラシ（samples/flyers/flyer-01〜03）と同じ商品・味違い・内容量違い。同じ商品は、チラシの注文番号にそろえる
        // 同じ商品：やさいせんべい（flyer-01）・ミルクビスケット（flyer-02）・いちごゼリー・ヨーグルトゼリー（flyer-03）
        // 味違い：ポテトチップス うすしお（flyer-02 の のりしお。flyer-02 の「うすしお ポテトチップス 大袋」は名前の似た別商品）。内容量違い：さくさくクッキー（flyer-02 は5袋入）・小魚あられ・海苔せんべい（flyer-01）
        p["yasai-senbei"] = Product("ひだまり やさいせんべい", "ひだまり米菓", "8枚（個包装）", 8, SnackType.Salty, 213, "8111", flavor: "せんべい");
        Confirmed(p["yasai-senbei"], 20, "うるち米（国産）、かぼちゃ、ほうれん草、にんじん、砂糖、食塩", []);
        p["usushio"] = Product("CO・OP ポテトチップス うすしお", "ひだまり生協", "18g×6袋", 6, SnackType.Salty, 257, "7213", flavor: "スナック");
        Confirmed(p["usushio"], 20, "じゃがいも（国産）、植物油、食塩", []);
        p["sakusaku-cookie"] = Product("CO・OP さくさくクッキー", "ひだまり生協", "10袋入", 10, SnackType.Sweet, 410, "7214", flavor: "焼き菓子");
        Confirmed(p["sakusaku-cookie"], 20, "小麦粉、砂糖、バター、鶏卵、食塩／膨張剤、（一部に小麦・卵・乳成分を含む）", ["wheat", "egg", "milk"]);
        p["ichigo-jelly"] = Product("うさぎのミミ いちごゼリー", "うさぎ乳業", "6個入", 6, SnackType.Smooth, 278, "431", character: "うさぎのミミ", flavor: "ゼリー");
        Confirmed(p["ichigo-jelly"], 100, "果糖ぶどう糖液糖、いちご果汁、ゲル化剤（増粘多糖類）、酸味料、香料", []);
        p["kozakana"] = Product("にこにこ 小魚あられ", "にこにこフーズ", "4g×3袋", 3, SnackType.Salty, 222, "7215", flavor: "せんべい");
        Reference(p["kozakana"], 30, AllergenCheckSource.Flyer, "表記なし", []);
        p["milk-biscuit"] = Product("くまのポッケ ミルクビスケット", "もりのこ製菓", "120g（10袋）", 10, SnackType.Sweet, 354, "8103", character: "くまのポッケ", flavor: "焼き菓子");
        Confirmed(p["milk-biscuit"], 20, "小麦粉、砂糖、ショートニング、全粉乳、食塩／膨張剤、（一部に小麦・乳成分を含む）", ["wheat", "milk"]);
        p["yogurt-jelly"] = Product("しろくまの ヨーグルトゼリー", "うさぎ乳業", "6個入", 6, SnackType.Smooth, 318, "8131", character: "しろくま", flavor: "ゼリー");
        Confirmed(p["yogurt-jelly"], 20, "砂糖、乳製品、ゲル化剤（増粘多糖類）、酸味料、香料、（一部に乳成分を含む）", ["milk"]);
        p["umi-senbei"] = Product("まるまる 海苔せんべい", "ひだまり米菓", "12枚", 12, SnackType.Salty, 248, "7204", flavor: "せんべい");
        Confirmed(p["umi-senbei"], 20, "うるち米、しょうゆ、のり、砂糖（一部に小麦・大豆を含む）", ["wheat", "soybean"]);

        // チラシにない商品（確認済み・参考・不明・要再確認・未確認の品目あり・使わない が混ざる）
        p["okome-snack"] = Product("もぐもぐ おこめスナック", "ひだまり米菓", "10袋", 10, SnackType.Salty, 298, "7201", flavor: "スナック");
        Confirmed(p["okome-snack"], 20, "うるち米（国産）、植物油、砂糖、食塩", []);
        p["mushipan"] = Product("ふんわり 蒸しパン", "ふわり洋菓子", "5個", 5, SnackType.Sweet, 268, "7203", flavor: "パン");
        Confirmed(p["mushipan"], 20, "小麦粉、砂糖、鶏卵、牛乳、植物油／膨張剤、（一部に小麦・卵・乳成分を含む）", ["wheat", "egg", "milk"]);
        p["kokutou-fugashi"] = Product("ほっこり 黒糖ふ菓子", "やまぶき屋", "8本", 8, SnackType.Sweet, 198, "7205", flavor: "焼き菓子");
        Confirmed(p["kokutou-fugashi"], 20, "小麦粉、黒糖、砂糖、水あめ（一部に小麦を含む）", ["wheat"]);
        p["mikan-jelly-old"] = Product("ぷるぷる みかんゼリー", "うさぎ乳業", "6個入", 6, SnackType.Smooth, 268, "7206", flavor: "ゼリー");
        p["coconut-sable"] = Product("ころころ ココナッツサブレ", "ことり堂", "8枚", 8, SnackType.Sweet, 298, "7207", flavor: "焼き菓子");
        Confirmed(p["coconut-sable"], 20, "小麦粉、砂糖、ココナッツファイン、バター（一部に小麦・乳成分を含む）", ["wheat", "milk"]);
        p["almond-cookie"] = Product("わくわく アーモンドクッキー", "もりのこ製菓", "10枚", 10, SnackType.Sweet, 358, "7208", flavor: "焼き菓子");
        Reference(p["almond-cookie"], 30, AllergenCheckSource.CoopSite, "コープのサイトの原材料", ["almond", "wheat", "milk"]);
        p["star-ramune"] = Product("きらきら 星のラムネ", "ことり堂", "4g×10袋", 10, SnackType.Sweet, 188, "7209", flavor: "キャンディ");
        Reference(p["star-ramune"], 30, AllergenCheckSource.CoopSite, "コープのサイトの原材料", []);
        p["imo-chips"] = Product("ぽかぽか さつまいもチップス", "ぐんぐん製菓", "15g×4袋", 4, SnackType.Salty, 208, "7210", flavor: "スナック");
        p["kanten"] = Product("きらきら フルーツ寒天", "みどり果樹園", "6個入", 6, SnackType.Smooth, 298, "7211", flavor: "ゼリー");
        ConfirmedMissing(p["kanten"], 20, "砂糖、みかん、もも、寒天、酸味料", ["peach"], "pine_nut");
        p["kinako-cookie"] = Product("どんぐり きなこクッキー", "やまぶき屋", "10枚", 10, SnackType.Sweet, 248, "7212", flavor: "焼き菓子");
        Confirmed(p["kinako-cookie"], 20, "小麦粉、きな粉、砂糖、植物油（一部に小麦・大豆を含む）", ["wheat", "soybean"]);
        p["kinako-cookie"].IsDisabled = true;
        p["mugicha"] = Product("CO・OP 麦茶（国産六条大麦）", "ひだまり生協", "125ml×18", 18, SnackType.NotSnack, 777, "7216", flavor: "お茶");
        return p;
    }

    private Product Product(string name, string maker, string volume, int pieces, SnackType snackType, int price, string orderNo, string? character = null, string? flavor = null)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var product = new Product
        {
            Name = name,
            NormalizedName = TextNormalizer.ForProductMatch(name),
            Maker = maker,
            Volume = volume,
            PiecesPerPack = pieces,
            SnackType = snackType,
            Price = price,
            PriceExcludingTax = (int)Math.Floor(price / 1.08m),
            OrderNo = orderNo,
            ListedWeekStart = JapanDate.WeekStart(_today).AddDays(-14),
            UpdateDateTime = now,
            UpdateUserId = _userId,
        };
        if (character is not null)
        {
            product.Tags.Add(new ProductTag { ProductId = product.Id, Kind = ProductTagKind.Character, Value = character, Source = ProductTagSource.Staff, UpdateDateTime = now });
        }

        if (flavor is not null)
        {
            product.Tags.Add(new ProductTag { ProductId = product.Id, Kind = ProductTagKind.Flavor, Value = flavor, Source = ProductTagSource.Staff, UpdateDateTime = now });
        }

        return product;
    }

    /// <summary>確認済みの記録（29品目と施設の追加品目のすべてを確定）</summary>
    private void Confirmed(Product product, int daysAgo, string ingredients, string[] containsCodes)
    {
        AddCheck(product, daysAgo, AllergenCheckStatus.Confirmed, AllergenCheckSource.Package, null, ingredients,
            _allergens.Select(a => (a.Id, containsCodes.Contains(a.Code))));
    }

    /// <summary>確認済みだが、あとで避ける品目に足した品目を確かめていない記録（未確認の品目あり）</summary>
    private void ConfirmedMissing(Product product, int daysAgo, string ingredients, string[] containsCodes, string missingCode)
    {
        AddCheck(product, daysAgo, AllergenCheckStatus.Confirmed, AllergenCheckSource.Package, null, ingredients,
            _allergens.Where(a => a.Code != missingCode).Select(a => (a.Id, containsCodes.Contains(a.Code))));
    }

    /// <summary>参考の記録（「含む」の品目だけ）</summary>
    private void Reference(Product product, int daysAgo, AllergenCheckSource source, string note, string[] containsCodes)
    {
        AddCheck(product, daysAgo, AllergenCheckStatus.Reference, source, note, null,
            _allergens.Where(a => containsCodes.Contains(a.Code)).Select(a => (a.Id, true)));
    }

    private void AddCheck(Product product, int daysAgo, AllergenCheckStatus status, AllergenCheckSource source, string? note, string? ingredients, IEnumerable<(string AllergenId, bool Contains)> items)
    {
        var checkedAt = JapanDate.StartOfDayUtc(_today.AddDays(-daysAgo)).AddHours(1);
        var check = new AllergenCheck
        {
            ProductId = product.Id,
            Status = status,
            Source = source,
            SourceNote = note,
            RawIngredients = ingredients,
            CheckedAt = checkedAt,
            CheckedBy = _checker,
            UpdateDateTime = checkedAt,
            UpdateUserId = _userId,
        };
        check.Items = items.Select(i => new AllergenCheckItem
        {
            AllergenCheckId = check.Id,
            AllergenId = i.AllergenId,
            Contains = i.Contains,
            UpdateDateTime = checkedAt,
            UpdateUserId = _userId,
        }).ToList();
        product.AllergenChecks.Add(check);
    }

    /// <summary>確定した献立（月〜金。日ごとの商品は掲載品の写しの価格で作る）</summary>
    private Menu ConfirmedMenu(DateOnly weekStart, Dictionary<string, Product> products, string[][] dayItems)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var menu = new Menu
        {
            WeekStart = weekStart,
            WeeklyBudget = 20000,
            Status = MenuStatus.Confirmed,
            ConfirmedAt = JapanDate.StartOfDayUtc(weekStart.AddDays(-3)),
            ConfirmedBy = _checker,
            UpdateDateTime = now,
            UpdateUserId = _userId,
        };
        var keys = dayItems.SelectMany(d => d).Distinct().ToList();
        menu.ListedProducts = keys.Select(k => new ListedProduct(products[k].Id, products[k].Price, products[k].OrderNo)).ToList();

        for (var i = 0; i < dayItems.Length; i++)
        {
            var day = new MenuDay
            {
                MenuId = menu.Id,
                Date = weekStart.AddDays(i),
                ChildrenCount = 50,
                ChildrenCountSource = ChildrenCountSource.Default,
                DayType = DayType.Normal,
                UpdateDateTime = now,
            };
            foreach (var key in dayItems[i])
            {
                var product = products[key];
                var created = MenuItemFactory.Create(day, product, menu.ListedProducts.First(l => l.ProductId == product.Id));
                var item = created.Value!;
                item.UpdateDateTime = now;
                day.Items.Add(item);
            }

            menu.Days.Add(day);
        }

        return menu;
    }

    /// <summary>E2E 用の下書き（再来週）：掲載品の写しだけを持ち、献立案はまだない</summary>
    private Menu DraftWithListing(DateOnly weekStart, Dictionary<string, Product> products)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var menu = new Menu
        {
            WeekStart = weekStart,
            WeeklyBudget = 20000,
            Status = MenuStatus.Draft,
            UpdateDateTime = now,
            UpdateUserId = _userId,
        };
        var keys = new[] { "yasai-senbei", "usushio", "sakusaku-cookie", "ichigo-jelly", "kozakana", "milk-biscuit", "okome-snack", "yogurt-jelly", "mushipan", "umi-senbei", "kokutou-fugashi", "mikan-jelly-old", "coconut-sable", "almond-cookie", "star-ramune", "kanten", "mugicha" };
        menu.ListedProducts = keys.Select(k => new ListedProduct(products[k].Id, products[k].Price, products[k].OrderNo)).ToList();
        for (var i = 0; i < 5; i++)
        {
            menu.Days.Add(new MenuDay
            {
                MenuId = menu.Id,
                Date = weekStart.AddDays(i),
                ChildrenCount = 50,
                ChildrenCountSource = ChildrenCountSource.Default,
                DayType = DayType.Normal,
                UpdateDateTime = now,
            });
        }

        return menu;
    }
}
