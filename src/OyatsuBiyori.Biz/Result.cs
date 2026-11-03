namespace OyatsuBiyori.Biz;

/// <summary>
/// ApplicationService の戻り値。入力チェックや業務エラーは例外にせず、日本語のメッセージで返す。
/// </summary>
public class Result
{
    /// <summary>エラーメッセージ（日本語。次に何をすればよいかを含める）</summary>
    public List<string> ErrorMessages { get; } = [];

    /// <summary>エラーがあるか</summary>
    public bool HasError => ErrorMessages.Count > 0;

    /// <summary>エラーを1つ持つ結果を作る</summary>
    public static Result Error(string message)
    {
        var result = new Result();
        result.ErrorMessages.Add(message);
        return result;
    }
}

/// <summary>
/// 値を返す ApplicationService の戻り値。
/// </summary>
public class Result<T> : Result
{
    /// <summary>結果の値（エラーのときは既定値）</summary>
    public T? Value { get; set; }

    /// <summary>値を持つ結果を作る</summary>
    public static Result<T> Ok(T value)
    {
        return new Result<T> { Value = value };
    }

    /// <summary>エラーを1つ持つ結果を作る</summary>
    public static new Result<T> Error(string message)
    {
        var result = new Result<T>();
        result.ErrorMessages.Add(message);
        return result;
    }
}
