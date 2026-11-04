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
}
