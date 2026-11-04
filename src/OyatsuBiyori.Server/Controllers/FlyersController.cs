using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// チラシの取り込み（SC-09 手順1。T3-04〜T3-10）。画像は Claude に送るだけで保存しない。
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public sealed class FlyersController(FlyerImportApplicationService flyerService) : ControllerBase
{
    /// <summary>1ファイルの上限（ブラウザで縮めてから送る。NFR-UX-02a）</summary>
    private const long MaxFileBytes = 20L * 1024 * 1024;

    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    private readonly FlyerImportApplicationService _flyerService = flyerService ?? throw new ArgumentNullException(nameof(flyerService));

    /// <summary>読み取りを受け付ける（バックグラウンド。処理の ID を返す）</summary>
    [HttpPost]
    public async Task<IActionResult> Read([FromForm] string? menuId, [FromForm] List<IFormFile>? files)
    {
        files ??= [];
        if (files.Count > FlyerImportApplicationService.MaxImages)
        {
            return new JsonResult(Result<string>.Error($"1回に読み取れるのは{FlyerImportApplicationService.MaxImages}枚までです。いらない画像を外してください"));
        }

        var images = new List<ClaudeImage>();
        foreach (var file in files)
        {
            if (file.Length == 0 || file.Length > MaxFileBytes)
            {
                return new JsonResult(Result<string>.Error($"「{file.FileName}」は大きすぎるか空です（20MB まで）。小さい画像にして、もう一度選んでください"));
            }

            if (!AllowedTypes.Contains(file.ContentType))
            {
                return new JsonResult(Result<string>.Error($"「{file.FileName}」は画像ではありません。写真を選び直してください（PDF は画面でページごとの画像にしてから送ります）"));
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            images.Add(new ClaudeImage(stream.ToArray(), file.ContentType));
        }

        return new JsonResult(await _flyerService.StartReadingAsync(menuId, images));
    }

    /// <summary>取り込みの状態（読み取りの処理と、確定前の結果）</summary>
    [HttpGet]
    public async Task<IActionResult> Latest([FromQuery] string? menuId) => new JsonResult(await _flyerService.GetLatestAsync(menuId));

    /// <summary>1商品を確定する（選び直しも）</summary>
    [HttpPost]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest request)
        => new JsonResult(await _flyerService.ConfirmItemAsync(new FlyerConfirmInput(
            request.MenuId,
            request.ItemId,
            request.Choice,
            request.ProductId,
            request.Name,
            request.Maker,
            request.Volume,
            request.PiecesPerPack,
            request.PiecesEdited,
            request.SnackType,
            request.SnackTypeEdited,
            request.PriceIncludingTax,
            request.PriceExcludingTax,
            request.OrderNo,
            request.TargetAgeLabel,
            request.CharacterTags ?? [],
            request.FlavorTags ?? [],
            request.LabelAllergenIds ?? [],
            request.ResolvedTerms ?? [],
            request.TreatAsFlyerError)));

    /// <summary>「同じ商品」（完全一致）をまとめて確定する</summary>
    [HttpPost]
    public async Task<IActionResult> ConfirmSame([FromBody] MenuRequest request) => new JsonResult(await _flyerService.ConfirmAllSameAsync(request.MenuId));

    /// <summary>手入力の商品を足す</summary>
    [HttpPost]
    public async Task<IActionResult> AddManual([FromBody] ManualRequest request, CancellationToken cancellationToken)
        => new JsonResult(await _flyerService.AddManualAsync(request.MenuId, new FlyerManualInput(
            request.Name,
            request.Maker,
            request.Volume,
            request.PriceIncludingTax,
            request.PriceExcludingTax,
            request.OrderNo,
            request.PiecesPerPack,
            request.SnackType,
            request.AllergenLabelText), cancellationToken));

    /// <summary>確定前の読み取り結果を捨てる</summary>
    [HttpPost]
    public IActionResult Discard([FromBody] MenuRequest request) => new JsonResult(_flyerService.Discard(request.MenuId));

    /// <summary>献立の指定</summary>
    public sealed class MenuRequest
    {
        public string? MenuId { get; set; }
    }

    /// <summary>確定の入力</summary>
    public sealed class ConfirmRequest
    {
        public string? MenuId { get; set; }

        public string? ItemId { get; set; }

        public FlyerItemChoice Choice { get; set; }

        public string? ProductId { get; set; }

        public string? Name { get; set; }

        public string? Maker { get; set; }

        public string? Volume { get; set; }

        public int? PiecesPerPack { get; set; }

        public bool PiecesEdited { get; set; }

        public SnackType? SnackType { get; set; }

        public bool SnackTypeEdited { get; set; }

        public int? PriceIncludingTax { get; set; }

        public int? PriceExcludingTax { get; set; }

        public string? OrderNo { get; set; }

        public string? TargetAgeLabel { get; set; }

        public List<string>? CharacterTags { get; set; }

        public List<string>? FlavorTags { get; set; }

        public List<string>? LabelAllergenIds { get; set; }

        public List<string>? ResolvedTerms { get; set; }

        public bool TreatAsFlyerError { get; set; }
    }

    /// <summary>手入力の商品</summary>
    public sealed class ManualRequest
    {
        public string? MenuId { get; set; }

        public string? Name { get; set; }

        public string? Maker { get; set; }

        public string? Volume { get; set; }

        public int? PriceIncludingTax { get; set; }

        public int? PriceExcludingTax { get; set; }

        public string? OrderNo { get; set; }

        public int? PiecesPerPack { get; set; }

        public SnackType? SnackType { get; set; }

        public string? AllergenLabelText { get; set; }
    }
}
