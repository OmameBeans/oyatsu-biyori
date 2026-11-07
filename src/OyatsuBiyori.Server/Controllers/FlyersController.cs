using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// チラシの取り込み（SC-09 手順1。D-101、09-flyer-import.md）。画像は Claude に送るだけで保存しない。
/// 分類（使えない / 再入力 / 取り込める）はサーバー（Biz）が決めて返し、「取り込みを終える」の API でも求め直す（画面の値を信用しない。09 の 3.1・5.1）。
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

    /// <summary>
    /// 取り込みの状態（読み取りの処理と、確定前の結果）。分類・理由・注意・一致した商品・タブの件数・取り込める件数は、呼ぶたびにサーバーが求め直す（09 の 3.1・6）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Latest([FromQuery] string? menuId)
        => new JsonResult(await _flyerService.GetLatestAsync(menuId));

    /// <summary>
    /// 行を直す（フォームの行のすべての値を送る。今の値と違う項目だけが直した項目になる）。分類し直した行を返す（09 の 5.3）。
    /// 取り込み済みの行も直せる（値が変わるなら、直す前に取り込みを取り消す。もう一度「取り込みを終える」で取り込み直す）
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateItem([FromBody] UpdateItemRequest request)
        => new JsonResult(await _flyerService.UpdateItemAsync(new FlyerItemEditInput(
            request.MenuId,
            request.ItemId,
            request.Name,
            request.Maker,
            request.Volume,
            request.PriceIncludingTax,
            request.PriceExcludingTax,
            request.OrderNo,
            request.PiecesPerPack,
            request.SnackType,
            request.TargetAgeLabel,
            request.CharacterTags,
            request.FlavorTags,
            request.LabelAllergenIds)));

    /// <summary>品目の分からない語に品目を選ぶ（空なら「対応する品目なし」。選び直しもこれ。FR-IMPORT-33）</summary>
    [HttpPost]
    public async Task<IActionResult> ResolveTerm([FromBody] ResolveTermRequest request)
        => new JsonResult(await _flyerService.ResolveTermAsync(request.MenuId, request.ItemId, request.Term, request.AllergenIds));

    /// <summary>「使えない（入数が分からない）」の商品に入数を入れて分類し直す（注意「読み取りの検証を通っていません」が出る。FR-IMPORT-11）</summary>
    [HttpPost]
    public async Task<IActionResult> EnterPieces([FromBody] EnterPiecesRequest request)
        => new JsonResult(await _flyerService.EnterPiecesAsync(request.MenuId, request.ItemId, request.PiecesPerPack));

    /// <summary>外した掲載品に移す。取り込み済みの商品なら、取り込みを取り消してから外す（FR-IMPORT-21）</summary>
    [HttpPost]
    public async Task<IActionResult> Remove([FromBody] ItemRequest request)
        => new JsonResult(await _flyerService.RemoveItemAsync(request.MenuId, request.ItemId));

    /// <summary>外した掲載品を一覧に戻す（分類し直した行を返す。FR-IMPORT-21）</summary>
    [HttpPost]
    public async Task<IActionResult> Restore([FromBody] ItemRequest request)
        => new JsonResult(await _flyerService.RestoreItemAsync(request.MenuId, request.ItemId));

    /// <summary>
    /// 「取り込みを終える」（09 の 5.1）。分類をサーバーで求め直し、「取り込める」の商品をすべて1回で取り込む
    /// （新しい商品は登録、登録済みの商品は上書き）。使えない・再入力は取り込まない。1件でも失敗したら何も保存しない
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Finish([FromBody] MenuRequest request)
        => new JsonResult(await _flyerService.FinishAsync(request.MenuId));

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

    /// <summary>1商品の指定</summary>
    public sealed class ItemRequest
    {
        public string? MenuId { get; set; }

        public string? ItemId { get; set; }
    }

    /// <summary>行を直す入力（フォームの行のすべての値。文字は空なら値なし。タグ・品目は null なら変えない）</summary>
    public sealed class UpdateItemRequest
    {
        public string? MenuId { get; set; }

        public string? ItemId { get; set; }

        public string? Name { get; set; }

        public string? Maker { get; set; }

        public string? Volume { get; set; }

        public int? PriceIncludingTax { get; set; }

        public int? PriceExcludingTax { get; set; }

        public string? OrderNo { get; set; }

        public int? PiecesPerPack { get; set; }

        public SnackType? SnackType { get; set; }

        public string? TargetAgeLabel { get; set; }

        public List<string>? CharacterTags { get; set; }

        public List<string>? FlavorTags { get; set; }

        public List<string>? LabelAllergenIds { get; set; }
    }

    /// <summary>品目の分からない語に選んだ品目（空なら「対応する品目なし」）</summary>
    public sealed class ResolveTermRequest
    {
        public string? MenuId { get; set; }

        public string? ItemId { get; set; }

        public string? Term { get; set; }

        public List<string>? AllergenIds { get; set; }
    }

    /// <summary>入数を入れる</summary>
    public sealed class EnterPiecesRequest
    {
        public string? MenuId { get; set; }

        public string? ItemId { get; set; }

        public int? PiecesPerPack { get; set; }
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
