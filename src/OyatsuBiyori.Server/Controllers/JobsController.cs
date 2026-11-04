using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// バックグラウンドの処理の状態（画面は2秒ごとに取りに行く。NFR-PERF-05）。見つからない ID は「失敗（再起動）」を返す。
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
}
