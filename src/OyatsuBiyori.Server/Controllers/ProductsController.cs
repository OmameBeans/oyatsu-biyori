using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Products;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// 商品一覧（SC-06）・商品詳細と編集（SC-07）・アレルゲン確認（SC-08）。
/// 確認の記録は追加だけで、更新・削除の API は作らない（SR-07）。
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public sealed class ProductsController(ProductApplicationService productService) : ControllerBase
{
    /// <summary>1ファイルの上限（ブラウザで縮めてから送る）</summary>
    private const long MaxFileBytes = 20L * 1024 * 1024;

    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    private readonly ProductApplicationService _productService = productService ?? throw new ArgumentNullException(nameof(productService));

    /// <summary>商品一覧</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] ProductListFilter filter = ProductListFilter.All, [FromQuery] bool includeDisabled = false)
        => new JsonResult(await _productService.ListAsync(q, filter, includeDisabled));

    /// <summary>商品詳細</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? id) => new JsonResult(await _productService.GetAsync(id));

    /// <summary>基本情報を直す</summary>
    [HttpPost]
    public async Task<IActionResult> Update([FromBody] UpdateRequest request)
        => new JsonResult(await _productService.UpdateAsync(new ProductUpdateInput(
            request.ProductId,
            request.Name,
            request.Maker,
            request.Volume,
            request.PiecesPerPack,
            request.DefaultPerChildCount,
            request.Price,
            request.JanCode,
            request.SnackType,
            request.Memo,
            request.IsDisabled,
            request.CharacterTags ?? [],
            request.FlavorTags ?? [])));

    /// <summary>編集中の原材料の全文で、探す言葉に当たった範囲</summary>
    [HttpPost]
    public async Task<IActionResult> KeywordHits([FromBody] KeywordHitsRequest request) => new JsonResult(await _productService.FindKeywordHitsAsync(request.Text));

    /// <summary>原材料表示の読み取りを受け付ける（バックグラウンド）</summary>
    [HttpPost]
    public async Task<IActionResult> Extract([FromForm] string? productId, [FromForm] List<IFormFile>? files)
    {
        files ??= [];
        if (files.Count > ProductApplicationService.MaxImages)
        {
            return new JsonResult(Result<string>.Error($"1回に読み取れるのは{ProductApplicationService.MaxImages}枚までです。いらない写真を外してください"));
        }

        var images = new List<ClaudeImage>();
        foreach (var file in files)
        {
            if (file.Length == 0 || file.Length > MaxFileBytes || !AllowedTypes.Contains(file.ContentType))
            {
                return new JsonResult(Result<string>.Error($"「{file.FileName}」は読めない形式か、大きすぎます（20MB まで）。写真を選び直してください"));
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            images.Add(new ClaudeImage(stream.ToArray(), file.ContentType));
        }

        return new JsonResult(await _productService.StartExtractionAsync(productId, images));
    }

    /// <summary>アレルゲン確認の記録を足す（追加のみ）</summary>
    [HttpPost]
    public async Task<IActionResult> RecordCheck([FromBody] RecordCheckRequest request)
        => new JsonResult(await _productService.RecordCheckAsync(new AllergenCheckInput(
            request.ProductId,
            request.Source,
            request.SourceNote,
            request.RawIngredients,
            request.ContaminationNote,
            (request.Items ?? []).Select(i => new AllergenItemDecision(i.AllergenId ?? string.Empty, i.Contains)).ToList(),
            request.CharacterTags ?? [],
            request.FlavorTags ?? [])));

    /// <summary>基本情報の入力</summary>
    public sealed class UpdateRequest
    {
        public string? ProductId { get; set; }

        public string? Name { get; set; }

        public string? Maker { get; set; }

        public string? Volume { get; set; }

        public int? PiecesPerPack { get; set; }

        public int? DefaultPerChildCount { get; set; }

        public int? Price { get; set; }

        public string? JanCode { get; set; }

        public SnackType SnackType { get; set; }

        public string? Memo { get; set; }

        public bool IsDisabled { get; set; }

        public List<string>? CharacterTags { get; set; }

        public List<string>? FlavorTags { get; set; }
    }

    /// <summary>全文</summary>
    public sealed class KeywordHitsRequest
    {
        public string? Text { get; set; }
    }

    /// <summary>品目の「含む / 含まない」</summary>
    public sealed class ItemDecisionRequest
    {
        public string? AllergenId { get; set; }

        public bool Contains { get; set; }
    }

    /// <summary>確認の記録の入力</summary>
    public sealed class RecordCheckRequest
    {
        public string? ProductId { get; set; }

        public AllergenCheckSource Source { get; set; }

        public string? SourceNote { get; set; }

        public string? RawIngredients { get; set; }

        public string? ContaminationNote { get; set; }

        public List<ItemDecisionRequest>? Items { get; set; }

        public List<string>? CharacterTags { get; set; }

        public List<string>? FlavorTags { get; set; }
    }
}
