using Microsoft.AspNetCore.Diagnostics;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Extensions;
using OyatsuBiyori.Claude;
using OyatsuBiyori.Data;
using OyatsuBiyori.Server;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ServerServiceCollectionExtensions.MaxRequestBodySize);

builder.Services.AddOpenApi();
builder.Services.AddBiz();
builder.Services.AddData(builder.Configuration);
builder.Services.AddClaude(builder.Configuration);
builder.Services.AddServerFoundation();

var app = builder.Build();

// 開発時だけマイグレーションを自動で当てる（本番は手元で作った SQL を当てる。D-42）
await app.Services.InitializeDatabaseAsync(applyMigrations: app.Environment.IsDevelopment());

// 予期しないエラーはログに残し、API には日本語の Result の形で返す（個人情報・画像はログに出さない）
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OyatsuBiyori.Server");
    logger.LogError(error, "処理中にエラーが起きました: {Path}", context.Request.Path);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(Result.Error("サーバーでエラーが起きました。しばらくしてから、もう一度試してください。"));
}));

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// 存在しない API に index.html を返さない（画面のルートだけ index.html に戻す）
app.Map("/api/{**path}", () => Results.NotFound(Result.Error("その操作は見つかりませんでした。画面を読み込み直してください。")));
app.MapFallbackToFile("/index.html");

app.Run();
