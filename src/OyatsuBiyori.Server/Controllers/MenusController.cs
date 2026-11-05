using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Menus;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// 献立一覧（SC-10）と献立作成（SC-09 の手順2〜4）。
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public sealed class MenusController(MenuApplicationService menuService, MenuProposalApplicationService proposalService) : ControllerBase
{
    private readonly MenuApplicationService _menuService = menuService ?? throw new ArgumentNullException(nameof(menuService));
    private readonly MenuProposalApplicationService _proposalService = proposalService ?? throw new ArgumentNullException(nameof(proposalService));

    /// <summary>献立一覧</summary>
    [HttpGet]
    public async Task<IActionResult> List() => new JsonResult(await _menuService.ListAsync());

    /// <summary>献立の中身</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? id) => new JsonResult(await _menuService.GetAsync(id));

    /// <summary>献立の下書きを作る（その週の献立があれば、それを返す）</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest request) => new JsonResult(await _menuService.CreateAsync(request.WeekStart));

    /// <summary>下書きを削除する</summary>
    [HttpPost]
    public async Task<IActionResult> Delete([FromBody] MenuRequest request) => new JsonResult(await _menuService.DeleteAsync(request.MenuId));

    /// <summary>手順2 今週の商品</summary>
    [HttpGet]
    public async Task<IActionResult> ListedProducts([FromQuery] string? id) => new JsonResult(await _menuService.GetListedProductsAsync(id));

    /// <summary>「今週は使わない」を切り替える</summary>
    [HttpPost]
    public async Task<IActionResult> SetExcluded([FromBody] ExcludedRequest request)
        => new JsonResult(await _menuService.SetExcludedAsync(request.MenuId, request.ProductId, request.Excluded));

    /// <summary>税込価格・入数をその場で入れる</summary>
    [HttpPost]
    public async Task<IActionResult> SetListedValues([FromBody] ListedValuesRequest request)
        => new JsonResult(await _menuService.SetListedValuesAsync(request.MenuId, request.ProductId, request.Price, request.PiecesPerPack));

    /// <summary>要望の文章を保存する</summary>
    [HttpPost]
    public async Task<IActionResult> SaveRequest([FromBody] RequestTextRequest request) => new JsonResult(await _menuService.SaveRequestAsync(request.MenuId, request.RequestText));

    /// <summary>献立案の作成を受け付ける（バックグラウンド）</summary>
    [HttpPost]
    public async Task<IActionResult> StartProposal([FromBody] RequestTextRequest request)
        => new JsonResult(await _proposalService.StartProposalAsync(request.MenuId, request.RequestText));

    /// <summary>献立案の作成・組み直しの、いちばん新しい処理</summary>
    [HttpGet]
    public IActionResult LatestProposalJob([FromQuery] string? id)
        => new JsonResult(Result<BackgroundJobStatusView>.Ok(new BackgroundJobStatusView(
            _proposalService.GetLatestJob(id, MenuProposalApplicationService.ProposalJobKind),
            _proposalService.GetLatestJob(id, MenuProposalApplicationService.ChatJobKind))));

    /// <summary>チャットでの組み直しを受け付ける（バックグラウンド）</summary>
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request) => new JsonResult(await _proposalService.StartChatAsync(request.MenuId, request.Message));

    /// <summary>予算に合わせる案を受け付ける（バックグラウンド）</summary>
    [HttpPost]
    public async Task<IActionResult> BudgetFit([FromBody] MenuRequest request) => new JsonResult(await _proposalService.StartBudgetFitAsync(request.MenuId));

    /// <summary>入れ替え案を反映する</summary>
    [HttpPost]
    public async Task<IActionResult> ApplyBudgetFit([FromBody] ApplyBudgetFitRequest request)
    {
        var result = await _proposalService.ApplyBudgetFitAsync(request.MenuId, new BudgetFitOptionInput(
            request.Kind,
            request.Date,
            request.MenuItemId,
            request.NewProductId ?? string.Empty,
            request.NewPackCount,
            request.CurrentTotal));
        if (result.HasError)
        {
            return new JsonResult(result);
        }

        return new JsonResult(await _menuService.GetAsync(request.MenuId));
    }

    /// <summary>週の予算を上書きする</summary>
    [HttpPost]
    public async Task<IActionResult> UpdateBudget([FromBody] BudgetRequest request)
        => new JsonResult(await _menuService.UpdateBudgetAsync(request.MenuId, request.Budget, request.RowVersion));

    /// <summary>日の人数・種類・見出しを直す</summary>
    [HttpPost]
    public async Task<IActionResult> UpdateDay([FromBody] DayRequest request)
        => new JsonResult(await _menuService.UpdateDayAsync(request.MenuId, request.Date, request.ChildrenCount, request.DayType, request.Label, request.UpdateLabel, request.RowVersion));

    /// <summary>商品を足す</summary>
    [HttpPost]
    public async Task<IActionResult> AddItem([FromBody] ItemRequest request)
        => new JsonResult(await _menuService.AddItemAsync(request.MenuId, request.Date, request.ProductId, request.RowVersion));

    /// <summary>商品を入れ替える</summary>
    [HttpPost]
    public async Task<IActionResult> ReplaceItem([FromBody] ItemRequest request)
        => new JsonResult(await _menuService.ReplaceItemAsync(request.MenuId, request.ItemId, request.ProductId, request.RowVersion));

    /// <summary>商品を外す</summary>
    [HttpPost]
    public async Task<IActionResult> RemoveItem([FromBody] ItemRequest request)
        => new JsonResult(await _menuService.RemoveItemAsync(request.MenuId, request.ItemId, request.RowVersion));

    /// <summary>商品を別の日へ動かす</summary>
    [HttpPost]
    public async Task<IActionResult> MoveItem([FromBody] ItemRequest request)
        => new JsonResult(await _menuService.MoveItemAsync(request.MenuId, request.ItemId, request.Date, request.RowVersion));

    /// <summary>1人あたりの個数・パック数を変える</summary>
    [HttpPost]
    public async Task<IActionResult> UpdateItem([FromBody] ItemRequest request)
        => new JsonResult(await _menuService.UpdateItemAsync(request.MenuId, request.ItemId, request.PerChildCount, request.PackCount, request.RowVersion));

    /// <summary>確定のダイアログの中身</summary>
    [HttpGet]
    public async Task<IActionResult> Confirmation([FromQuery] string? id) => new JsonResult(await _menuService.GetConfirmationAsync(id));

    /// <summary>確定する</summary>
    [HttpPost]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest request)
        => new JsonResult(await _menuService.ConfirmAsync(request.MenuId, request.Acknowledgements ?? [], request.RowVersion));

    /// <summary>警告の商品を確認し直す（確定済みのまま）</summary>
    [HttpPost]
    public async Task<IActionResult> Acknowledge([FromBody] AcknowledgeRequest request)
        => new JsonResult(await _menuService.AcknowledgeAsync(request.MenuId, new AcknowledgementInput(request.ItemId, request.Kind, request.Fingerprint)));

    /// <summary>献立の指定</summary>
    public class MenuRequest
    {
        public string? MenuId { get; set; }
    }

    /// <summary>作る週</summary>
    public sealed class CreateRequest
    {
        public DateOnly? WeekStart { get; set; }
    }

    /// <summary>「今週は使わない」</summary>
    public sealed class ExcludedRequest : MenuRequest
    {
        public string? ProductId { get; set; }

        public bool Excluded { get; set; }
    }

    /// <summary>価格・入数</summary>
    public sealed class ListedValuesRequest : MenuRequest
    {
        public string? ProductId { get; set; }

        public int? Price { get; set; }

        public int? PiecesPerPack { get; set; }
    }

    /// <summary>要望の文章</summary>
    public sealed class RequestTextRequest : MenuRequest
    {
        public string? RequestText { get; set; }
    }

    /// <summary>チャットの依頼</summary>
    public sealed class ChatRequest : MenuRequest
    {
        public string? Message { get; set; }
    }

    /// <summary>週の予算</summary>
    public sealed class BudgetRequest : MenuRequest
    {
        public int? Budget { get; set; }

        public string? RowVersion { get; set; }
    }

    /// <summary>日の値</summary>
    public sealed class DayRequest : MenuRequest
    {
        public DateOnly Date { get; set; }

        public int? ChildrenCount { get; set; }

        public DayType? DayType { get; set; }

        public string? Label { get; set; }

        public bool UpdateLabel { get; set; }

        public string? RowVersion { get; set; }
    }

    /// <summary>日ごとの商品の操作</summary>
    public sealed class ItemRequest : MenuRequest
    {
        public DateOnly Date { get; set; }

        public string? ItemId { get; set; }

        public string? ProductId { get; set; }

        public int? PerChildCount { get; set; }

        public int? PackCount { get; set; }

        public string? RowVersion { get; set; }
    }

    /// <summary>確定</summary>
    public sealed class ConfirmRequest : MenuRequest
    {
        public List<AcknowledgementInput>? Acknowledgements { get; set; }

        public string? RowVersion { get; set; }
    }

    /// <summary>確認し直し</summary>
    public sealed class AcknowledgeRequest : MenuRequest
    {
        public string? ItemId { get; set; }

        public AllergenJudgementKind Kind { get; set; }

        public string? Fingerprint { get; set; }
    }

    /// <summary>入れ替え案の反映</summary>
    public sealed class ApplyBudgetFitRequest : MenuRequest
    {
        public BudgetFitKind Kind { get; set; }

        public DateOnly Date { get; set; }

        public string? MenuItemId { get; set; }

        public string? NewProductId { get; set; }

        public int NewPackCount { get; set; }

        public int CurrentTotal { get; set; }
    }

    /// <summary>献立案の作成（Job）とチャットの組み直し（ChatJob）の、いちばん新しい処理（なければ null）</summary>
    public sealed record BackgroundJobStatusView(OyatsuBiyori.Biz.Interfaces.BackgroundJobStatus? Job, OyatsuBiyori.Biz.Interfaces.BackgroundJobStatus? ChatJob);
}
