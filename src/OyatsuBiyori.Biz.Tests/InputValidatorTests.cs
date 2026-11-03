using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Tests;

public class InputValidatorTests
{
    private static SettingsInput ValidSettings() => new(
        50,
        12500,
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        90,
        [new DuplicateAvoidRule(SnackType.Salty, 30), new DuplicateAvoidRule(SnackType.Sweet, 30)]);

    [Fact]
    public void 正しい条件設定は通る()
    {
        Assert.False(InputValidator.ValidateSettings(ValidSettings()).HasError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void 通常の人数は1以上(int? value)
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { DefaultChildrenCount = value });

        Assert.Contains("通常の人数は1以上の整数で入れてください", result.ErrorMessages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void 既定の週の予算は1以上(int? value)
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { DefaultWeeklyBudget = value });

        Assert.Contains("既定の週の予算は1以上の整数で入れてください", result.ErrorMessages);
    }

    [Fact]
    public void 再確認期間は1以上()
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { RecheckDays = 0 });

        Assert.True(result.HasError);
    }

    [Fact]
    public void 出す曜日は1つ以上()
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { ServingWeekdays = [] });

        Assert.Contains("おやつを出す曜日を1つ以上選んでください", result.ErrorMessages);
    }

    [Fact]
    public void 出す曜日の重なりはエラー()
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { ServingWeekdays = [DayOfWeek.Monday, DayOfWeek.Monday] });

        Assert.True(result.HasError);
    }

    [Fact]
    public void 範囲外の曜日はエラー()
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { ServingWeekdays = [(DayOfWeek)7] });

        Assert.Contains("おやつを出す曜日が正しくありません。選び直してください", result.ErrorMessages);
    }

    [Fact]
    public void 条件設定でも対象外の分類の重複の期間はエラー()
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { DuplicateAvoidDays = [new DuplicateAvoidRule(SnackType.NotSnack, 30)] });

        Assert.Contains("「対象外」の分類には重複させない期間を設定できません", result.ErrorMessages);
    }

    [Fact]
    public void 重複の期間は設定なしの分類があってもよい()
    {
        var result = InputValidator.ValidateSettings(ValidSettings() with { DuplicateAvoidDays = [] });

        Assert.False(result.HasError);
    }

    [Fact]
    public void 対象外の分類に重複の期間は設定できない()
    {
        var errors = InputValidator.DuplicateRuleErrors([new DuplicateAvoidRule(SnackType.NotSnack, 30)]);

        Assert.Contains("「対象外」の分類には重複させない期間を設定できません", errors);
    }

    [Fact]
    public void 同じ分類の重複の期間は1件だけ()
    {
        var errors = InputValidator.DuplicateRuleErrors([new DuplicateAvoidRule(SnackType.Salty, 30), new DuplicateAvoidRule(SnackType.Salty, 60)]);

        Assert.Contains("「しょっぱいもの」の重複させない期間が2つあります。1つにしてください", errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void 重複の期間は1以上(int days)
    {
        var errors = InputValidator.DuplicateRuleErrors([new DuplicateAvoidRule(SnackType.Smooth, days)]);

        Assert.Single(errors);
    }

    [Fact]
    public void 決まっていない分類は例外にせずエラーにする()
    {
        var errors = InputValidator.DuplicateRuleErrors([new DuplicateAvoidRule((SnackType)99, 0)]);

        Assert.Equal(2, errors.Count);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(70, false)]
    [InlineData(-1, true)]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    public void 日ごとの人数は0以上で上限まで(int value, bool hasError)
    {
        Assert.Equal(hasError, InputValidator.ValidateDayChildrenCount(value).HasError);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(24, false)]
    public void 入数と1人あたりの個数と税込価格とパック数は1以上(int? value, bool hasError)
    {
        Assert.Equal(hasError, InputValidator.ValidatePiecesPerPack(value).HasError);
        Assert.Equal(hasError, InputValidator.ValidatePerChildCount(value).HasError);
        Assert.Equal(hasError, InputValidator.ValidatePrice(value).HasError);
        Assert.Equal(hasError, InputValidator.ValidatePackCount(value).HasError);
        Assert.Equal(hasError, InputValidator.ValidateWeeklyBudget(value).HasError);
    }

    [Theory]
    [InlineData("プラリネ", false)]
    [InlineData("ﾏｼﾞﾊﾟﾝ", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("・（）－", true)]
    [InlineData(null, true)]
    public void 探す言葉は正規化して空にならないこと(string? keyword, bool hasError)
    {
        Assert.Equal(hasError, InputValidator.ValidateKeyword(keyword).HasError);
    }

    [Theory]
    [InlineData("abcdefghijk", true)]
    [InlineData("abcdefghijkl", false)]
    [InlineData("おやつびより123456", false)]
    [InlineData("あいうえおかきくけこさ", true)]
    [InlineData("", true)]
    public void パスワードは12文字以上で種類は問わない(string password, bool hasError)
    {
        Assert.Equal(hasError, InputValidator.ValidatePassword(password, "staff").HasError);
    }

    [Fact]
    public void パスワードの文字数はサロゲートペアを1文字で数える()
    {
        // 𠮷 は2つの char だが1文字。11文字なので通らない
        var password = "𠮷𠮷𠮷𠮷𠮷𠮷𠮷𠮷𠮷𠮷𠮷";

        Assert.True(InputValidator.ValidatePassword(password, "staff").HasError);
    }

    [Theory]
    [InlineData("oyatsu-staff-01")]
    [InlineData("OYATSU-STAFF-01")]
    [InlineData("ｏｙａｔｓｕ-staff-01")]
    public void ユーザー名と同じパスワードは使えない(string password)
    {
        var result = InputValidator.ValidatePassword(password, "oyatsu-staff-01");

        Assert.Contains("ユーザー名と同じパスワードは使えません", result.ErrorMessages);
    }
}
