using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// バックグラウンドの処理の状態（画面は2秒ごとに取りに行く。NFR-PERF-05）。見つからない ID は「失敗（再起動）」を返す。
/// チャットの返答は、Stream で届いた順に流す（D-91）。
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public sealed class JobsController(IBackgroundJobService jobs) : ControllerBase
{
    private readonly IBackgroundJobService _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));

    /// <summary>処理の状態</summary>
    [HttpGet]
    public IActionResult Get([FromQuery] string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return new JsonResult(Result<BackgroundJobStatus>.Error("処理が指定されていません"));
        }

        return new JsonResult(Result<BackgroundJobStatus>.Ok(_jobs.Get(id) ?? BackgroundJobStatus.Lost(id)));
    }

    /// <summary>
    /// 処理の段階名と返答の文章を、変わるたびに流す（Server-Sent Events。チャットの返答をパラパラ出す）。
    /// 処理が終わったら最後の状態を送って閉じる。画面を離れても処理は止まらない（流すのをやめるだけ）。
    /// </summary>
    [HttpGet]
    public IResult Stream([FromQuery] string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(id))
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.ServerSentEvents(_jobs.StreamAsync(id, cancellationToken), eventType: "progress");
    }
}
