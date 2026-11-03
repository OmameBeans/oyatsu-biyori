using System.Reflection;

namespace OyatsuBiyori.Claude.Prompts;

/// <summary>
/// プロンプト（Prompts/*.md）を読む。プロンプトは EmbeddedResource として DLL に含める（publish で漏れないように）。
/// </summary>
internal static class PromptStore
{
    private static readonly Assembly Assembly = typeof(PromptStore).Assembly;

    /// <summary>すべての呼び出しの前に置く共通の前置き</summary>
    public static string Common => Get("Common");

    /// <summary>プロンプトを読む（name は Prompts フォルダのファイル名から .md を除いたもの）</summary>
    public static string Get(string name)
    {
        var resourceName = $"OyatsuBiyori.Claude.Prompts.{name}.md";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"プロンプトが見つかりません: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
