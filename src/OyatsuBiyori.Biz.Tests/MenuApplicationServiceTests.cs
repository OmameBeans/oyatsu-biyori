using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Menus;
using OyatsuBiyori.Biz.Tests.Fakes;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// 献立の確定（T4-11）・確認し直し（T4-13）・下書きの作成（T3-02）を、メモリ上のリポジトリで確かめる（SR-04・SR-21・SR-22）。
/// </summary>
public class MenuApplicationServiceTests
{
    private static (MenuApplicationService Service, InMemoryData Data) Create(InMemoryData data)
    {
        var time = new FixedTimeProvider(new DateTimeOffset(NoonUtc(Today)));
        var factory = new MenuContextFactory(new FakeProductRepository(data), new FakeSettingRepository(data), new FakeMenuRepository(data), time);
        var service = new MenuApplicationService(
            new FakeMenuRepository(data),
            new FakeProductRepository(data),
            new FakeSettingRepository(data),
            new FakeUnitOfWork(data),
            factory,
            new FakeUserContext(),
            time,
            new FakeJobs());
        return (service, data);
    }

    private static (InMemoryData Data, Menu Menu, MenuItem WarningItem) WarningMenu()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var warning = ReferenceOnly("ふわふわゼリー", SnackType.Smooth);
        data.Products.Add(warning);
        var menu = NewMenu(20000, 50, (warning, 300));
        var item = Put(menu, 0, warning);
        data.Menus.Add(menu);
        return (data, menu, item);
    }

    [Fact]
    public async Task SR_04_警告の商品を確かめないと確定できない()
    {
        var (data, menu, _) = WarningMenu();
        var (service, _) = Create(data);

        var result = await service.ConfirmAsync(menu.Id, [], null);

        Assert.True(result.HasError);
        Assert.Equal(MenuStatus.Draft, menu.Status);
    }

    [Fact]
    public async Task 画面で読んだ判定のまま確かめると確定できる()
    {
        var (data, menu, item) = WarningMenu();
        var (service, _) = Create(data);
        var confirmation = await service.GetConfirmationAsync(menu.Id);
        var ack = Assert.Single(confirmation.Value!.Acknowledgements);

        var result = await service.ConfirmAsync(menu.Id, [new AcknowledgementInput(item.Id, ack.Judgement.Kind, ack.Fingerprint)], null);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal(MenuStatus.Confirmed, menu.Status);
        Assert.NotNull(item.AcknowledgedAt);
    }

    [Fact]
    public async Task SR_22_画面で読んだ判定と今の判定が違えば_確認済みにしない()
    {
        var (data, menu, item) = WarningMenu();
        var (service, _) = Create(data);

        var wrongKind = await service.ConfirmAsync(menu.Id, [new AcknowledgementInput(item.Id, AllergenJudgementKind.RecheckNeeded, "古い指紋")], null);

        Assert.True(wrongKind.HasError);
        Assert.Null(item.AcknowledgedAt);
        Assert.Equal(MenuStatus.Draft, menu.Status);
    }

    [Fact]
    public async Task SR_22_同じ種類でも中身が違えば_確認済みにしない()
    {
        var (data, menu, item) = WarningMenu();
        var (service, _) = Create(data);
        var confirmation = await service.GetConfirmationAsync(menu.Id);
        var ack = Assert.Single(confirmation.Value!.Acknowledgements);

        // ダイアログを開いたあとで避ける品目が増えた（判定の中身が変わる）
        data.Avoid("milk");
        var result = await service.ConfirmAsync(menu.Id, [new AcknowledgementInput(item.Id, ack.Judgement.Kind, ack.Fingerprint)], null);

        Assert.True(result.HasError);
        Assert.Null(item.AcknowledgedAt);
    }

    [Fact]
    public async Task SR_21_使えない商品は確かめても確定できない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var blocked = Snack("たまごボーロ", SnackType.Sweet, 4, 1, ConfirmedAll(Today.AddDays(-5), ["egg"]));
        data.Products.Add(blocked);
        var menu = NewMenu(20000, 50, (blocked, 181));
        var item = Put(menu, 0, blocked);
        data.Menus.Add(menu);
        var (service, _) = Create(data);

        var result = await service.ConfirmAsync(menu.Id, [new AcknowledgementInput(item.Id, AllergenJudgementKind.Contains, "x")], null);
        var confirmation = await service.GetConfirmationAsync(menu.Id);

        Assert.True(result.HasError);
        Assert.Equal(MenuStatus.Draft, menu.Status);
        var blocking = Assert.Single(confirmation.Value!.BlockingIssues);
        Assert.NotNull(blocking.Judgement);
        Assert.Equal(AllergenJudgementCategory.Blocked, blocking.Judgement!.Category);
    }

    [Fact]
    public async Task 確定済みの献立で商品を直すと下書きに戻る()
    {
        var (data, menu, item) = WarningMenu();
        menu.Status = MenuStatus.Confirmed;
        var (service, _) = Create(data);

        var result = await service.UpdateItemAsync(menu.Id, item.Id, null, item.PackCount + 1, null);

        Assert.False(result.HasError);
        Assert.Equal(MenuStatus.Draft, menu.Status);
    }

    [Fact]
    public async Task FR_MENU_24_商品を別の日へ動かすと_価格と1人あたりの個数を保ち_先の日の人数でパック数を求め直す()
    {
        var data = new InMemoryData();
        var snack = Safe("ほしがたクッキー", SnackType.Sweet, pieces: 10, perChild: 2);
        data.Products.Add(snack);
        var menu = NewMenu(20000, 50, (snack, 300));
        var item = Put(menu, 0, snack);
        menu.Days.Single(d => d.Date == D(2)).ChildrenCount = 33;
        data.Menus.Add(menu);
        var (service, _) = Create(data);

        var result = await service.MoveItemAsync(menu.Id, item.Id, D(2), null);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Empty(menu.Days.Single(d => d.Date == D(0)).Items);
        var moved = Assert.Single(menu.Days.Single(d => d.Date == D(2)).Items);
        Assert.Same(item, moved);
        Assert.Equal(menu.Days.Single(d => d.Date == D(2)).Id, moved.MenuDayId);
        Assert.Equal(300, moved.UnitPrice);
        Assert.Equal(2, moved.PerChildCount);
        // 33人 × 2個 ÷ 10個入り = 6.6 → 切り上げて7パック
        Assert.Equal(7, moved.PackCount);
        Assert.Equal(2100, moved.Amount);
    }

    [Fact]
    public async Task FR_MENU_24_人数が同じ日へ動かすと_上書きしたパック数を残す()
    {
        var data = new InMemoryData();
        var snack = Safe("ほしがたクッキー", SnackType.Sweet, pieces: 10);
        data.Products.Add(snack);
        var menu = NewMenu(20000, 50, (snack, 300));
        var item = Put(menu, 0, snack);
        data.Menus.Add(menu);
        var (service, _) = Create(data);
        await service.UpdateItemAsync(menu.Id, item.Id, null, 8, null);

        var result = await service.MoveItemAsync(menu.Id, item.Id, D(1), null);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        // 50人 × 1個 ÷ 10個入り = 5 だが、職員が直した8パックのまま
        Assert.Equal(8, item.PackCount);
        Assert.Equal(2400, item.Amount);
    }

    [Fact]
    public async Task D_77_手作りの日へ動かすと_金額は0になる()
    {
        var data = new InMemoryData();
        var snack = Safe("ほしがたクッキー", SnackType.Sweet, pieces: 10);
        data.Products.Add(snack);
        var menu = NewMenu(20000, 50, (snack, 300));
        var item = Put(menu, 0, snack);
        menu.Days.Single(d => d.Date == D(4)).DayType = DayType.Handmade;
        data.Menus.Add(menu);
        var (service, _) = Create(data);

        var result = await service.MoveItemAsync(menu.Id, item.Id, D(4), null);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal(0, item.Amount);
        Assert.Equal(300, item.UnitPrice);
    }

    [Fact]
    public async Task FR_MENU_24_動かす先の日に同じ商品があれば_動かさない()
    {
        var data = new InMemoryData();
        var snack = Safe("ほしがたクッキー");
        data.Products.Add(snack);
        var menu = NewMenu(20000, 50, (snack, 300));
        var item = Put(menu, 0, snack);
        Put(menu, 1, snack);
        data.Menus.Add(menu);
        var (service, _) = Create(data);

        var result = await service.MoveItemAsync(menu.Id, item.Id, D(1), null);

        Assert.True(result.HasError);
        Assert.Contains(item, menu.Days.Single(d => d.Date == D(0)).Items);
        Assert.Single(menu.Days.Single(d => d.Date == D(1)).Items);
    }

    [Fact]
    public async Task FR_MENU_24_同じ日や献立にない日へは_動かさない()
    {
        var data = new InMemoryData();
        var snack = Safe("ほしがたクッキー");
        data.Products.Add(snack);
        var menu = NewMenu(20000, 50, (snack, 300));
        var item = Put(menu, 0, snack);
        data.Menus.Add(menu);
        var (service, _) = Create(data);

        var same = await service.MoveItemAsync(menu.Id, item.Id, D(0), null);
        var outside = await service.MoveItemAsync(menu.Id, item.Id, D(6), null);

        Assert.True(same.HasError);
        Assert.True(outside.HasError);
        Assert.Contains(item, menu.Days.Single(d => d.Date == D(0)).Items);
    }

    [Fact]
    public async Task SR_22_警告の商品を別の日へ動かすと_確定時の確認が消え_確定済みの献立は下書きに戻る()
    {
        var (data, menu, item) = WarningMenu();
        var (service, _) = Create(data);
        var confirmation = await service.GetConfirmationAsync(menu.Id);
        var ack = Assert.Single(confirmation.Value!.Acknowledgements);
        await service.ConfirmAsync(menu.Id, [new AcknowledgementInput(item.Id, ack.Judgement.Kind, ack.Fingerprint)], null);
        Assert.Equal(MenuStatus.Confirmed, menu.Status);
        Assert.NotNull(item.AcknowledgedAt);

        var result = await service.MoveItemAsync(menu.Id, item.Id, D(3), null);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Null(item.AcknowledgedAt);
        Assert.Null(item.AcknowledgedJudgementFingerprint);
        Assert.Equal(MenuStatus.Draft, menu.Status);
        // 確かめ直さないと、もう一度は確定できない
        var again = await service.ConfirmAsync(menu.Id, [], null);
        Assert.True(again.HasError);
        Assert.Equal(MenuStatus.Draft, menu.Status);
    }

    [Fact]
    public async Task SR_21_使えない商品は別の日へ動かしても_確定できない()
    {
        var data = new InMemoryData();
        data.Avoid("egg");
        var blocked = Snack("たまごボーロ", SnackType.Sweet, 4, 1, ConfirmedAll(Today.AddDays(-5), ["egg"]));
        data.Products.Add(blocked);
        var menu = NewMenu(20000, 50, (blocked, 181));
        var item = Put(menu, 0, blocked);
        data.Menus.Add(menu);
        var (service, _) = Create(data);

        var moved = await service.MoveItemAsync(menu.Id, item.Id, D(2), null);
        var confirmation = await service.GetConfirmationAsync(menu.Id);

        Assert.False(moved.HasError, string.Join(",", moved.ErrorMessages));
        Assert.False(confirmation.Value!.CanConfirm);
        var blocking = Assert.Single(confirmation.Value.BlockingIssues);
        Assert.Equal(D(2), blocking.Date);
        Assert.Equal(AllergenJudgementCategory.Blocked, blocking.Judgement!.Category);
    }

    [Fact]
    public async Task 確定済みのまま確認し直せる_判定が変わっていれば止める()
    {
        var (data, menu, item) = WarningMenu();
        menu.Status = MenuStatus.Confirmed;
        var (service, _) = Create(data);
        var detail = await service.GetAsync(menu.Id);
        var view = detail.Value!.Days.SelectMany(d => d.Items).Single();

        var stale = await service.AcknowledgeAsync(menu.Id, new AcknowledgementInput(item.Id, view.Judgement!.Kind, "古い指紋"));
        var ok = await service.AcknowledgeAsync(menu.Id, new AcknowledgementInput(item.Id, view.Judgement.Kind, view.JudgementFingerprint));

        Assert.True(stale.HasError);
        Assert.False(ok.HasError);
        Assert.Equal(MenuStatus.Confirmed, menu.Status);
        Assert.NotNull(item.AcknowledgedAt);
    }

    [Fact]
    public async Task T3_02_通常の人数か既定の週の予算が未設定なら_献立を作らない()
    {
        var data = new InMemoryData();
        data.Setting.DefaultWeeklyBudget = null;
        var (service, _) = Create(data);

        var result = await service.CreateAsync(WeekStart.AddDays(2));

        Assert.True(result.HasError);
        Assert.Empty(data.Menus);
    }

    [Fact]
    public async Task T3_02_献立は週の初日にそろえ_出す曜日の日を通常の人数で作り_予算を写す()
    {
        var data = new InMemoryData();
        var (service, _) = Create(data);

        var result = await service.CreateAsync(WeekStart.AddDays(2));
        var again = await service.CreateAsync(WeekStart);

        var menu = Assert.Single(data.Menus);
        Assert.Equal(WeekStart, menu.WeekStart);
        Assert.Equal(20000, menu.WeeklyBudget);
        Assert.Equal(5, menu.Days.Count);
        Assert.All(menu.Days, d => Assert.Equal(50, d.ChildrenCount));
        Assert.Equal(result.Value, again.Value);
    }

    [Fact]
    public async Task FR_LIST_00_先の週の下書きを削除して間が空いても_その週の未作成の行が出る()
    {
        var data = new InMemoryData();
        var (service, _) = Create(data);
        var thisWeek = JapanDate.WeekStart(Today);
        var threeWeeksLater = await service.CreateAsync(thisWeek.AddDays(21));
        await service.CreateAsync(thisWeek.AddDays(28));
        await service.DeleteAsync(threeWeeksLater.Value);

        var list = await service.ListAsync();

        var weeks = list.Value!.Rows.Select(r => r.WeekStart).ToList();
        Assert.Equal([thisWeek.AddDays(28), thisWeek.AddDays(21), thisWeek.AddDays(14), thisWeek.AddDays(7), thisWeek], weeks);
        var gap = list.Value.Rows.Single(r => r.WeekStart == thisWeek.AddDays(21));
        Assert.Null(gap.MenuId);
        Assert.Null(gap.RelativeLabel);
    }

    [Fact]
    public async Task FR_LIST_00_献立がなければ_今週_来週_再来週の行だけが出る()
    {
        var (service, _) = Create(new InMemoryData());
        var thisWeek = JapanDate.WeekStart(Today);

        var list = await service.ListAsync();

        Assert.Equal([thisWeek.AddDays(14), thisWeek.AddDays(7), thisWeek], list.Value!.Rows.Select(r => r.WeekStart));
    }
}
