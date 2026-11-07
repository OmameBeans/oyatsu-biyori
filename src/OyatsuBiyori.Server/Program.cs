using Microsoft.AspNetCore.Diagnostics;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Extensions;
using OyatsuBiyori.Claude;
using OyatsuBiyori.Data;
using OyatsuBiyori.Server;
using OyatsuBiyori.Server.DemoData;
using OyatsuBiyori.Server.E2E;
using OyatsuBiyori.Server.Security;

var builder = WebApplication.CreateBuilder(args);

// 読み取り精度の測定では、結果の Markdown にログが混ざらないよう警告以上だけを出す
var isMeasuring = args.Contains("--measure-reading");
if (isMeasuring)
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ServerServiceCollectionExtensions.MaxRequestBodySize);

builder.Services.AddOpenApi();
builder.Services.AddBiz();
builder.Services.AddData(builder.Configuration);
builder.Services.AddClaude(builder.Configuration);
builder.Services.AddServerFoundation();
builder.Services.AddServerSecurity(builder.Configuration, builder.Environment);

// E2E の環境だけ、偽の Claude とダミーデータの投入を登録する（本番では登録しない。T0-15）
builder.Services.AddE2EOverrides(builder.Environment, builder.Configuration);

var app = builder.Build();

// 読み取り精度の測定（T3-11）：架空のチラシ・パッケージを本物の Claude で読み取り、正解データと比べる。開発の環境だけ。
// 「--out <ファイル>」を付けると、結果の Markdown をそのファイルに書く
if (isMeasuring)
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    try
    {
        var report = await app.Services.MeasureReadingAsync(app.Environment, app.Configuration, CancellationToken.None);
        var outIndex = Array.IndexOf(args, "--out");
        if (outIndex >= 0 && outIndex + 1 < args.Length)
        {
            await File.WriteAllTextAsync(args[outIndex + 1], report);
            Console.WriteLine("測定の結果を書きました：" + Path.GetFullPath(args[outIndex + 1]));
        }
        else
        {
            Console.WriteLine(report);
        }
    }
    catch (Exception ex)
    {
        // 開発用の道具なので、理由だけを出して終わる（API キーの値は例外の文に含めない）
        Console.Error.WriteLine("測定できません：" + ex.Message);
        Environment.ExitCode = 1;
    }

    return;
}

// デモ環境の作り直し（開発の環境だけ。本番では DemoDataGuard が止める。TD-02）
if (args.Contains("--reset-demo"))
{
    await app.Services.ResetDemoAsync(app.Environment, app.Configuration);
    Console.WriteLine("デモ環境を作り直しました（ダミーデータを入れました）。");
    return;
}

// 献立だけをダミーに戻す（開発の環境だけ。商品・確認の記録・条件設定・アカウントはそのまま）
if (args.Contains("--reset-menus"))
{
    await app.Services.ResetDemoMenusAsync(app.Environment, app.Configuration);
    Console.WriteLine("献立をダミーに戻しました。");
    return;
}

// E2E の環境では、DB を毎回作り直してダミーデータを入れる
await app.Services.ResetE2EDatabaseIfNeededAsync(app.Environment, app.Configuration);

// 開発時と E2E のときだけマイグレーションを自動で当てる（本番は手元で作った SQL を当てる。D-42）
await app.Services.InitializeDatabaseAsync(applyMigrations: app.Environment.IsDevelopment() || E2EEnvironment.IsE2E(app.Environment));

// 初期アカウントの準備・ロックの解除（FR-AUTH-03・05）
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AccountBootstrapper>().RunAsync();
}

await app.Services.SeedE2EDataIfNeededAsync(app.Environment, app.Configuration);

// 予期しないエラーはログに残し、API には日本語の Result の形で返す（個人情報・画像はログに出さない）
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OyatsuBiyori.Server");
    logger.LogError(error, "処理中にエラーが起きました: {Path}", context.Request.Path);

    if (error is OyatsuBiyori.Biz.Interfaces.ConcurrencyConflictException conflict)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(Result.Error(conflict.Message));
        return;
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(Result.Error("サーバーでエラーが起きました。しばらくしてから、もう一度試してください。"));
}));

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// 画面のファイルとランディングは、ログインしていなくても配る（API は FallbackPolicy でログインを求める）
app.MapStaticAssets().AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();

// 存在しない API に index.html を返さない（画面のルートだけ index.html に戻す）
app.Map("/api/{**path}", () => Results.NotFound(Result.Error("エラーが発生しました。読み込み直してください。")));
app.MapFallbackToFile("/index.html").AllowAnonymous();

app.Run();
