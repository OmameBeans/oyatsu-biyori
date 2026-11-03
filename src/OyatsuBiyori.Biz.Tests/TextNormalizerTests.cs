using OyatsuBiyori.Biz;

namespace OyatsuBiyori.Biz.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("ＣＯ・ＯＰ ポテトチップス うすしお", "ぽてとちっぷすうすしお")]
    [InlineData("コープ ポテトチップス　うすしお", "ぽてとちっぷすうすしお")]
    [InlineData("ﾎﾟﾃﾄﾁｯﾌﾟｽ うすしお", "ぽてとちっぷすうすしお")]
    [InlineData("CO-OP ポテトチップス", "ぽてとちっぷす")]
    [InlineData("もぐもぐクッキー（ミルク味）", "もぐもぐくっきーみるく味")]
    public void 商品照合用の正規化は表記ゆれとコープの表記をそろえる(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.ForProductMatch(input));
    }

    [Fact]
    public void 探す言葉の正規化ではコープを除かない()
    {
        Assert.Equal("こーぷ", TextNormalizer.ForKeywordSearch("コープ"));
    }

    [Theory]
    [InlineData("ヘーゼルナッツペースト", "へーぜるなっつぺーすと")]
    [InlineData("ﾍｰｾﾞﾙﾅｯﾂ", "へーぜるなっつ")]
    [InlineData("ヘーゼル\nナッツ", "へーぜるなっつ")]
    [InlineData("ヘーゼル\r\nナッツ", "へーぜるなっつ")]
    [InlineData("ヘーゼル　ナッツ", "へーぜるなっつ")]
    [InlineData("小麦粉、砂糖、（一部に乳・小麦を含む）", "小麦粉砂糖一部に乳小麦を含む")]
    public void 探す言葉の正規化は全角半角とカタカナと空白改行記号をそろえる(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.ForKeywordSearch(input));
    }

    [Fact]
    public void 言葉と全文を同じ正規化にすると改行をまたいでも当たる()
    {
        var keyword = TextNormalizer.ForKeywordSearch("ヘーゼルナッツ");
        var text = TextNormalizer.ForKeywordSearch("砂糖、植物油脂、ﾍｰｾﾞﾙ\nナッツペースト、ココア");

        Assert.Contains(keyword, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ヘ－ゼルナッツペースト")]
    [InlineData("ヘ‐ゼルナッツペースト")]
    [InlineData("ヘ―ゼルナッツペースト")]
    [InlineData("ヘ−ゼルナッツペースト")]
    [InlineData("ヘ一ゼルナッツペースト")]
    [InlineData("ﾍｰｾﾞﾙﾅｯﾂﾍﾟｰｽﾄ")]
    [InlineData("へーぜるなっつぺーすと")]
    [InlineData("HAZELNUT ヘーゼルナッツ")]
    public void 長音の書き違いやひらがなでも探す言葉に当たる(string text)
    {
        var keyword = TextNormalizer.ForKeywordSearch("ヘーゼルナッツ");

        Assert.Contains(keyword, TextNormalizer.ForKeywordSearch(text), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("クルミ", "くるみ")]
    [InlineData("ピーナツ", "ぴーなつ")]
    [InlineData("Hazelnut", "HAZELNUT")]
    public void カタカナとひらがなや大文字と小文字は同じになる(string left, string right)
    {
        Assert.Equal(TextNormalizer.ForKeywordSearch(left), TextNormalizer.ForKeywordSearch(right));
    }

    [Fact]
    public void 全角の濁点が離れて書かれていても前の文字とまとめる()
    {
        Assert.Equal(TextNormalizer.ForKeywordSearch("ペカン"), TextNormalizer.ForKeywordSearch("ヘ゜カン"));
    }

    [Fact]
    public void かな以外の後ろのハイフンや漢数字は長音にしない()
    {
        Assert.Equal("40g1個", TextNormalizer.ForKeywordSearch("40g-1個"));
        Assert.Equal("小麦一部", TextNormalizer.ForKeywordSearch("小麦一部"));
    }

    [Fact]
    public void 当たった位置を元の全文の範囲に戻せる()
    {
        const string original = "砂糖、ヘーゼル\nナッツペースト、ココア";
        var normalized = TextNormalizer.ForKeywordSearchWithMap(original);
        var keyword = TextNormalizer.ForKeywordSearch("ヘーゼルナッツ");

        var index = normalized.Text.IndexOf(keyword, StringComparison.Ordinal);
        var (start, length) = normalized.ToOriginalRange(index, keyword.Length);

        Assert.Equal("ヘーゼル\nナッツ", original.Substring(start, length));
    }

    [Fact]
    public void 半角カタカナの濁点も元の範囲に含める()
    {
        const string original = "原材料：ﾍｰｾﾞﾙﾅｯﾂ、砂糖";
        var normalized = TextNormalizer.ForKeywordSearchWithMap(original);
        var keyword = TextNormalizer.ForKeywordSearch("ヘーゼルナッツ");

        var index = normalized.Text.IndexOf(keyword, StringComparison.Ordinal);
        var (start, length) = normalized.ToOriginalRange(index, keyword.Length);

        Assert.Equal("ﾍｰｾﾞﾙﾅｯﾂ", original.Substring(start, length));
    }

    [Theory]
    [InlineData("40g（2袋×12個包装）", "40g(2袋x12個包装)")]
    [InlineData("40g(2袋x12個包装)", "40g(2袋x12個包装)")]
    [InlineData("４ｇ × ３袋", "4gx3袋")]
    [InlineData("100ml*18", "100mlx18")]
    public void 内容量の正規化は全角半角とかける記号と括弧をそろえる(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.ForVolume(input));
    }

    [Fact]
    public void 空やnullは空文字になる()
    {
        Assert.Equal(string.Empty, TextNormalizer.ForProductMatch(null));
        Assert.Equal(string.Empty, TextNormalizer.ForKeywordSearch(string.Empty));
        Assert.Equal(string.Empty, TextNormalizer.ForVolume(null));
    }

    [Fact]
    public void 記号だけの言葉は正規化すると空になる()
    {
        // 空の言葉はすべての商品に当たってしまうため、登録時に拒む（T1-21）
        Assert.Equal(string.Empty, TextNormalizer.ForKeywordSearch("・・・ "));
    }
}
