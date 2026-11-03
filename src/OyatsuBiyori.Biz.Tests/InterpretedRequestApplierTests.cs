using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;

namespace OyatsuBiyori.Biz.Tests;

public class InterpretedRequestApplierTests
{
    private const int DefaultChildren = 50;

    private static InterpretedRequests Request(params InterpretedDay[] days) => new() { Days = [.. days] };

    [Fact]
    public void 要望の人数と日の種類と見出しを反映する()
    {
        var menu = NewMenu(20000, DefaultChildren);

        var result = InterpretedRequestApplier.ApplyRequest(menu, Request(
            new InterpretedDay(D(2), 30, null, null),
            new InterpretedDay(D(3), null, DayType.Handmade, null),
            new InterpretedDay(D(4), null, null, "誕生日会")), DefaultChildren, _ => 10);

        Assert.Equal(30, menu.Days[2].ChildrenCount);
        Assert.Equal(ChildrenCountSource.FromRequest, menu.Days[2].ChildrenCountSource);
        Assert.Equal(DayType.Handmade, menu.Days[3].DayType);
        Assert.Equal("誕生日会", menu.Days[4].Label);
        Assert.Equal(3, result.Applied.Count);
        Assert.Empty(result.NotApplied);
        Assert.Equal(3, menu.InterpretedRequests!.Days.Count);
    }

    [Fact]
    public void 職員が直した値は要望で上書きしない()
    {
        var menu = NewMenu(20000, DefaultChildren);
        menu.Days[2].ChildrenCount = 45;
        menu.Days[2].ChildrenCountSource = ChildrenCountSource.EditedByStaff;
        menu.Days[3].DayType = DayType.NoSnack;
        menu.Days[3].DayTypeEdited = true;
        menu.Days[4].Label = "ハロウィン会";
        menu.Days[4].LabelEdited = true;

        var result = InterpretedRequestApplier.ApplyRequest(menu, Request(
            new InterpretedDay(D(2), 30, null, null),
            new InterpretedDay(D(3), null, DayType.Handmade, null),
            new InterpretedDay(D(4), null, null, "誕生日会")), DefaultChildren, _ => 10);

        Assert.Equal(45, menu.Days[2].ChildrenCount);
        Assert.Equal(ChildrenCountSource.EditedByStaff, menu.Days[2].ChildrenCountSource);
        Assert.Equal(DayType.NoSnack, menu.Days[3].DayType);
        Assert.Equal("ハロウィン会", menu.Days[4].Label);
        Assert.Empty(result.Applied);
        Assert.Equal(3, result.KeptStaffEdits.Count);
    }

    [Fact]
    public void 要望を直したら前の要望で入れた値は既定に戻す()
    {
        var menu = NewMenu(20000, DefaultChildren);
        InterpretedRequestApplier.ApplyRequest(menu, Request(
            new InterpretedDay(D(2), 30, DayType.Handmade, "都民の日")), DefaultChildren, _ => 10);

        InterpretedRequestApplier.ApplyRequest(menu, Request(new InterpretedDay(D(4), null, null, "誕生日会")), DefaultChildren, _ => 10);

        Assert.Equal(DefaultChildren, menu.Days[2].ChildrenCount);
        Assert.Equal(ChildrenCountSource.Default, menu.Days[2].ChildrenCountSource);
        Assert.Equal(DayType.Normal, menu.Days[2].DayType);
        Assert.Null(menu.Days[2].Label);
        Assert.Equal("誕生日会", menu.Days[4].Label);
    }

    [Fact]
    public void 献立にない日付と負の人数は反映せず読み取りの反映しなかった要望も返す()
    {
        var menu = NewMenu(20000, DefaultChildren);
        var interpreted = Request(
            new InterpretedDay(D(5), 30, null, null),
            new InterpretedDay(D(1), -3, null, null));
        interpreted.Rejected.Add(new RejectedRequest("卵の子はお休みなので卵を使ってよい", "アレルギーの条件は要望では緩められません"));

        var result = InterpretedRequestApplier.ApplyRequest(menu, interpreted, DefaultChildren, _ => 10);

        Assert.Equal(3, result.NotApplied.Count);
        Assert.Equal("卵の子はお休みなので卵を使ってよい", result.NotApplied[0].Text);
        Assert.Equal(DefaultChildren, menu.Days[1].ChildrenCount);
        Assert.Equal(3, menu.InterpretedRequests!.Rejected.Count);
    }

    [Fact]
    public void 同じ要望をもう一度当てても変えた値はない()
    {
        var menu = NewMenu(20000, DefaultChildren);
        var request = Request(new InterpretedDay(D(2), 30, DayType.Handmade, "都民の日"));
        InterpretedRequestApplier.ApplyRequest(menu, request, DefaultChildren, _ => 10);

        var again = InterpretedRequestApplier.ApplyRequest(menu, request, DefaultChildren, _ => 10);

        Assert.Empty(again.Applied);
        Assert.Equal(30, menu.Days[2].ChildrenCount);
    }

    [Fact]
    public void 消した要望で戻した値は変えた値に含める()
    {
        var menu = NewMenu(20000, DefaultChildren);
        InterpretedRequestApplier.ApplyRequest(menu, Request(new InterpretedDay(D(2), 30, null, null)), DefaultChildren, _ => 10);

        var result = InterpretedRequestApplier.ApplyRequest(menu, Request(), DefaultChildren, _ => 10);

        var change = Assert.Single(result.Applied);
        Assert.Equal(InterpretedField.ChildrenCount, change.Field);
        Assert.Equal("30", change.OldValue);
        Assert.Equal(DefaultChildren.ToString(), change.NewValue);
    }

    [Fact]
    public void 出どころが通常の日の人数は要望を直しても今の設定の人数に戻さない()
    {
        var menu = NewMenu(20000, 45);

        InterpretedRequestApplier.ApplyRequest(menu, Request(), DefaultChildren, _ => 10);

        Assert.All(menu.Days, d => Assert.Equal(45, d.ChildrenCount));
    }

    [Fact]
    public void こう理解しましたには献立にない日と受け付けなかった人数を残さない()
    {
        var menu = NewMenu(20000, DefaultChildren);

        InterpretedRequestApplier.ApplyRequest(menu, Request(
            new InterpretedDay(D(5), 30, null, null),
            new InterpretedDay(D(1), -3, null, "遠足")), DefaultChildren, _ => 10);

        var day = Assert.Single(menu.InterpretedRequests!.Days);
        Assert.Equal(D(1), day.Date);
        Assert.Null(day.ChildrenCount);
        Assert.Equal("遠足", day.Label);
    }

    [Fact]
    public void チャットでも献立にない日付と負の人数は反映しない()
    {
        var menu = NewMenu(20000, DefaultChildren);

        var result = InterpretedRequestApplier.ApplyChat(menu,
            [new InterpretedDay(D(6), 30, null, null), new InterpretedDay(D(0), -1, null, null)],
            new HashSet<DateOnly> { D(0), D(6) }, _ => 10);

        Assert.Equal(2, result.NotApplied.Count);
        Assert.Empty(result.Applied);
        Assert.Equal(DefaultChildren, menu.Days[0].ChildrenCount);
        Assert.Empty(menu.InterpretedRequests!.Days);
    }

    [Fact]
    public void チャットで職員の値を残した項目は要望の値として記録しない()
    {
        var menu = NewMenu(20000, DefaultChildren);
        menu.Days[1].ChildrenCount = 45;
        menu.Days[1].ChildrenCountSource = ChildrenCountSource.EditedByStaff;

        InterpretedRequestApplier.ApplyChat(menu, [new InterpretedDay(D(1), 35, null, "遠足")], new HashSet<DateOnly>(), _ => 10);

        var day = Assert.Single(menu.InterpretedRequests!.Days);
        Assert.Null(day.ChildrenCount);
        Assert.Equal("遠足", day.Label);
    }

    [Fact]
    public void 極端な人数は反映せず例外にもしない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, DefaultChildren, (cookie, 300));
        Put(menu, 1, cookie);

        var result = InterpretedRequestApplier.ApplyRequest(menu, Request(new InterpretedDay(D(1), int.MaxValue, null, null)), DefaultChildren, _ => 10);

        Assert.Single(result.NotApplied);
        Assert.Equal(DefaultChildren, menu.Days[1].ChildrenCount);
    }

    [Fact]
    public void 人数が変わった日はパック数を式で求め直す()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, DefaultChildren, (cookie, 300));
        var item = Put(menu, 2, cookie);
        var other = Put(menu, 3, cookie);
        MenuItemFactory.SetPackCount(other, menu.Days[3], 9);

        InterpretedRequestApplier.ApplyRequest(menu, Request(new InterpretedDay(D(2), 61, null, null)), DefaultChildren, _ => 10);

        Assert.Equal(7, item.PackCount);
        Assert.Equal(7 * 300, item.Amount);
        Assert.Equal(9, other.PackCount);
    }

    [Fact]
    public void 手作りの日になったら商品の金額は0になる()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, DefaultChildren, (cookie, 300));
        var item = Put(menu, 3, cookie);

        InterpretedRequestApplier.ApplyRequest(menu, Request(new InterpretedDay(D(3), null, DayType.Handmade, null)), DefaultChildren, _ => 10);

        Assert.Equal(0, item.Amount);
    }

    [Fact]
    public void チャットでは前の値を戻さずに足し職員が直した値は明示の日だけ上書きする()
    {
        var menu = NewMenu(20000, DefaultChildren);
        InterpretedRequestApplier.ApplyRequest(menu, Request(new InterpretedDay(D(4), null, null, "誕生日会")), DefaultChildren, _ => 10);
        menu.Days[1].ChildrenCount = 45;
        menu.Days[1].ChildrenCountSource = ChildrenCountSource.EditedByStaff;
        menu.Days[2].ChildrenCount = 40;
        menu.Days[2].ChildrenCountSource = ChildrenCountSource.EditedByStaff;

        var result = InterpretedRequestApplier.ApplyChat(menu,
            [new InterpretedDay(D(1), 35, null, null), new InterpretedDay(D(2), 30, DayType.Handmade, null)],
            new HashSet<DateOnly> { D(2) }, _ => 10);

        Assert.Equal("誕生日会", menu.Days[4].Label);
        Assert.Equal(45, menu.Days[1].ChildrenCount);
        Assert.Equal(30, menu.Days[2].ChildrenCount);
        Assert.Equal(ChildrenCountSource.FromRequest, menu.Days[2].ChildrenCountSource);
        Assert.Equal(DayType.Handmade, menu.Days[2].DayType);
        Assert.Single(result.KeptStaffEdits);
        Assert.Equal([D(2), D(4)], menu.InterpretedRequests!.Days.Select(d => d.Date));
        Assert.Contains(menu.InterpretedRequests.Days, d => d.Date == D(2) && d.ChildrenCount == 30 && d.DayType == DayType.Handmade);
    }

    [Fact]
    public void チャットで職員が直した日の種類を明示的に変えたら職員が直した印を外す()
    {
        var menu = NewMenu(20000, DefaultChildren);
        menu.Days[0].DayType = DayType.NoSnack;
        menu.Days[0].DayTypeEdited = true;

        InterpretedRequestApplier.ApplyChat(menu, [new InterpretedDay(D(0), null, DayType.Normal, null)], new HashSet<DateOnly> { D(0) }, _ => 10);

        Assert.Equal(DayType.Normal, menu.Days[0].DayType);
        Assert.False(menu.Days[0].DayTypeEdited);
    }

    [Fact]
    public void 空の見出しは見出しを消す()
    {
        var menu = NewMenu(20000, DefaultChildren);
        menu.Days[0].Label = "誕生日会";

        InterpretedRequestApplier.ApplyChat(menu, [new InterpretedDay(D(0), null, null, "  ")], new HashSet<DateOnly>(), _ => 10);

        Assert.Null(menu.Days[0].Label);
    }
}
