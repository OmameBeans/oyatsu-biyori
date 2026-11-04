using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz.ApplicationServices;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// 条件設定（SC-03）と避ける品目（SC-04）。
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public sealed class SettingsController(SettingsApplicationService settingsService) : ControllerBase
{
    private readonly SettingsApplicationService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

    /// <summary>条件設定</summary>
    [HttpGet]
    public async Task<IActionResult> Get() => new JsonResult(await _settingsService.GetSettingsAsync());

    /// <summary>条件設定を保存する</summary>
    [HttpPost]
    public async Task<IActionResult> Update([FromBody] SettingsRequest request)
        => new JsonResult(await _settingsService.UpdateSettingsAsync(new SettingsUpdateInput(
            request.DefaultChildrenCount,
            request.DefaultWeeklyBudget,
            request.ServingWeekdays,
            request.RecheckDays,
            request.DuplicateAvoidDays,
            request.DuplicateRuleNote)));

    /// <summary>品目・グループ・避ける品目・探す言葉</summary>
    [HttpGet]
    public async Task<IActionResult> Allergens() => new JsonResult(await _settingsService.GetAllergenMasterAsync());

    /// <summary>避ける品目を足す</summary>
    [HttpPost]
    public async Task<IActionResult> AddAvoided([FromBody] AllergenRequest request) => new JsonResult(await _settingsService.AddAvoidedAsync(request.AllergenId));

    /// <summary>グループの品目をまとめて避ける品目に足す</summary>
    [HttpPost]
    public async Task<IActionResult> AddGroup([FromBody] GroupRequest request) => new JsonResult(await _settingsService.AddGroupAsync(request.GroupId));

    /// <summary>避ける品目を外す</summary>
    [HttpPost]
    public async Task<IActionResult> RemoveAvoided([FromBody] AllergenRequest request) => new JsonResult(await _settingsService.RemoveAvoidedAsync(request.AllergenId));

    /// <summary>探す言葉を足す</summary>
    [HttpPost]
    public async Task<IActionResult> AddKeyword([FromBody] KeywordRequest request) => new JsonResult(await _settingsService.AddKeywordAsync(request.AllergenId, request.Keyword));

    /// <summary>探す言葉を消す</summary>
    [HttpPost]
    public async Task<IActionResult> RemoveKeyword([FromBody] KeywordIdRequest request) => new JsonResult(await _settingsService.RemoveKeywordAsync(request.KeywordId));

    /// <summary>施設の追加品目を登録する</summary>
    [HttpPost]
    public async Task<IActionResult> CreateFacilityAllergen([FromBody] FacilityAllergenRequest request)
        => new JsonResult(await _settingsService.CreateFacilityAllergenAsync(request.Name, request.Keywords, request.Avoid));

    /// <summary>施設の追加品目を削除する</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteFacilityAllergen([FromBody] AllergenRequest request) => new JsonResult(await _settingsService.DeleteFacilityAllergenAsync(request.AllergenId));

    /// <summary>条件設定の入力</summary>
    public sealed class SettingsRequest
    {
        public int? DefaultChildrenCount { get; set; }

        public int? DefaultWeeklyBudget { get; set; }

        public List<DayOfWeek>? ServingWeekdays { get; set; }

        public int? RecheckDays { get; set; }

        public List<DuplicateAvoidRule>? DuplicateAvoidDays { get; set; }

        public string? DuplicateRuleNote { get; set; }
    }

    /// <summary>品目の指定</summary>
    public sealed class AllergenRequest
    {
        public string? AllergenId { get; set; }
    }

    /// <summary>グループの指定</summary>
    public sealed class GroupRequest
    {
        public string? GroupId { get; set; }
    }

    /// <summary>探す言葉の追加</summary>
    public sealed class KeywordRequest
    {
        public string? AllergenId { get; set; }

        public string? Keyword { get; set; }
    }

    /// <summary>探す言葉の指定</summary>
    public sealed class KeywordIdRequest
    {
        public string? KeywordId { get; set; }
    }

    /// <summary>施設の追加品目の登録</summary>
    public sealed class FacilityAllergenRequest
    {
        public string? Name { get; set; }

        public List<string>? Keywords { get; set; }

        public bool Avoid { get; set; } = true;
    }
}
