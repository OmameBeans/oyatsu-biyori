using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.Tests.Fakes;

/// <summary>
/// ApplicationService のテスト用のメモリ上のリポジトリ（DB を使わずに、サービスの配線を確かめる）。
/// </summary>
internal sealed class InMemoryData
{
    public Setting Setting { get; } = new() { RecheckDays = 90, ServingWeekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], DefaultChildrenCount = 50, DefaultWeeklyBudget = 20000 };

    public List<Allergen> Allergens { get; } = [.. TestData.AllAllergens];

    public List<AvoidedAllergen> Avoided { get; } = [];

    public List<Product> Products { get; } = [];

    public List<Menu> Menus { get; } = [];

    public List<MenuMessage> Messages { get; } = [];

    public List<AllergenCheck> AddedChecks { get; } = [];

    public int SaveCount { get; set; }

    /// <summary>true なら、保存のときに別の画面の更新とぶつかったことにする（ConcurrencyConflictException）</summary>
    public bool IsSaveConflicting { get; set; }

    public void Avoid(params string[] codes)
    {
        foreach (var code in codes)
        {
            var allergen = TestData.Allergen(code);
            Avoided.Add(new AvoidedAllergen { AllergenId = allergen.Id, Allergen = allergen });
        }
    }
}

internal sealed class FakeSettingRepository(InMemoryData data) : ISettingRepository
{
    public Task<Setting> GetSettingAsync() => Task.FromResult(data.Setting);

    public Task<IReadOnlyList<Allergen>> GetAllergensAsync() => Task.FromResult<IReadOnlyList<Allergen>>(data.Allergens);

    public Task<Allergen?> GetAllergenAsync(string allergenId) => Task.FromResult(data.Allergens.FirstOrDefault(a => a.Id == allergenId));

    public Task<IReadOnlyList<AllergenGroup>> GetGroupsAsync() => Task.FromResult<IReadOnlyList<AllergenGroup>>([]);

    public Task<IReadOnlyList<AvoidedAllergen>> GetAvoidedAsync() => Task.FromResult<IReadOnlyList<AvoidedAllergen>>(data.Avoided);

    public Task<IReadOnlyList<Allergen>> GetAvoidedAllergensAsync() => Task.FromResult<IReadOnlyList<Allergen>>(data.Avoided.Select(a => a.Allergen).ToList());

    public Task<AllergenKeyword?> GetKeywordAsync(string keywordId) => Task.FromResult<AllergenKeyword?>(null);

    public Task<bool> IsUsedByChecksAsync(string allergenId) => Task.FromResult(false);

    public Task<bool> IsInGroupAsync(string allergenId) => Task.FromResult(false);

    public void AddAvoided(AvoidedAllergen avoided) => data.Avoided.Add(avoided);

    public void RemoveAvoided(AvoidedAllergen avoided) => data.Avoided.Remove(avoided);

    public void AddAllergen(Allergen allergen) => data.Allergens.Add(allergen);

    public void RemoveAllergen(Allergen allergen) => data.Allergens.Remove(allergen);

    public void AddKeyword(AllergenKeyword keyword)
    {
    }

    public void RemoveKeyword(AllergenKeyword keyword)
    {
    }

    public void AddSafetyLog(SafetySettingLog log)
    {
    }
}

internal sealed class FakeProductRepository(InMemoryData data) : IProductRepository
{
    public Task<Product?> GetAsync(string productId) => Task.FromResult(data.Products.FirstOrDefault(p => p.Id == productId));

    public Task<IReadOnlyList<Product>> GetManyAsync(IEnumerable<string> productIds)
    {
        var ids = productIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<Product>>(data.Products.Where(p => ids.Contains(p.Id)).ToList());
    }

    public Task<IReadOnlyList<Product>> GetAllAsync() => Task.FromResult<IReadOnlyList<Product>>(data.Products.ToList());

    public Task<Product?> FindByJanAsync(string janCode) => Task.FromResult(data.Products.FirstOrDefault(p => p.JanCode == janCode));

    public void Add(Product product) => data.Products.Add(product);

    public void AddCheck(AllergenCheck check) => data.AddedChecks.Add(check);

    public void AddTag(ProductTag tag)
    {
    }

    public void RemoveTag(ProductTag tag)
    {
    }
}

internal sealed class FakeMenuRepository(InMemoryData data) : IMenuRepository
{
    public Task<Menu?> GetAsync(string menuId) => Task.FromResult(data.Menus.FirstOrDefault(m => m.Id == menuId));

    public Task<Menu?> GetByWeekStartAsync(DateOnly weekStart) => Task.FromResult(data.Menus.FirstOrDefault(m => m.WeekStart == weekStart));

    public Task<IReadOnlyList<Menu>> GetAllAsync() => Task.FromResult<IReadOnlyList<Menu>>(data.Menus.ToList());

    public Task<IReadOnlyList<Menu>> GetConfirmedInRangeAsync(DateOnly from, DateOnly to) =>
        Task.FromResult<IReadOnlyList<Menu>>(data.Menus.Where(m => m.Status == MenuStatus.Confirmed && m.Days.Any(d => d.Date >= from && d.Date <= to)).ToList());

    public Task<IReadOnlyList<Menu>> GetFromDateAsync(DateOnly from) => Task.FromResult<IReadOnlyList<Menu>>(data.Menus.Where(m => m.Days.Any(d => d.Date >= from)).ToList());

    public Task<IReadOnlyList<MenuMessage>> GetMessagesAsync(string menuId) => Task.FromResult<IReadOnlyList<MenuMessage>>(data.Messages.Where(m => m.MenuId == menuId).ToList());

    public void SetExpectedRowVersion(Menu menu, byte[] rowVersion)
    {
    }

    public Task RefreshRowVersionAsync(Menu menu) => Task.CompletedTask;

    public void Add(Menu menu) => data.Menus.Add(menu);

    public void Remove(Menu menu) => data.Menus.Remove(menu);

    public void RemoveItem(MenuItem item)
    {
        foreach (var day in data.Menus.SelectMany(m => m.Days))
        {
            day.Items.Remove(item);
        }
    }

    public void AddMessage(MenuMessage message) => data.Messages.Add(message);

    public void RemoveMessages(IEnumerable<MenuMessage> messages)
    {
        foreach (var message in messages.ToList())
        {
            data.Messages.Remove(message);
        }
    }
}

internal sealed class FakeUnitOfWork(InMemoryData data) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (data.IsSaveConflicting)
        {
            throw new ConcurrencyConflictException();
        }

        data.SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>ログインしている職員（loggedIn が false なら、ログインしていないことにする）</summary>
internal sealed class FakeUserContext(bool loggedIn = true) : IUserContext
{
    public Task<CurrentUserInfo?> GetCurrentUserInfoAsync() => Task.FromResult(loggedIn ? new CurrentUserInfo("user-1", "staff") : null);
}

internal sealed class FakeSessionStore : IFlyerSessionStore
{
    private readonly Dictionary<string, FlyerImportSession> _sessions = [];

    public FlyerImportSession? Get(string menuId) => _sessions.GetValueOrDefault(menuId);

    public void Set(FlyerImportSession session) => _sessions[session.MenuId] = session;

    public void Remove(string menuId) => _sessions.Remove(menuId);
}

/// <summary>処理をすぐにその場で動かす（テスト用）</summary>
internal sealed class FakeJobs : IBackgroundJobService
{
    public string Enqueue(string kind, string? ownerKey, CurrentUserInfo? user, TimeSpan deadline, BackgroundWork work) => Guid.NewGuid().ToString();

    public BackgroundJobStatus? Get(string id) => null;

    /// <summary>FindLatest で返す処理（種類と持ち主が合うときだけ返す）</summary>
    public BackgroundJobStatus? Latest { get; set; }

    public BackgroundJobStatus? FindLatest(string kind, string ownerKey) =>
        Latest is { } latest && latest.Kind == kind && latest.OwnerKey == ownerKey ? latest : null;

    /// <summary>FindLatestSucceeded で返す処理（未設定なら、Latest が終わっていればそれを返す）</summary>
    public BackgroundJobStatus? LatestSucceeded { get; set; }

    public BackgroundJobStatus? FindLatestSucceeded(string kind, string ownerKey) =>
        (LatestSucceeded ?? (Latest is { State: BackgroundJobState.Succeeded } ? Latest : null)) is { } s && s.Kind == kind && s.OwnerKey == ownerKey ? s : null;

    public IAsyncEnumerable<BackgroundJobStreamEvent> StreamAsync(string id, CancellationToken cancellationToken) => AsyncEnumerable.Empty<BackgroundJobStreamEvent>();
}

/// <summary>決まった読み取り結果を返す</summary>
internal sealed class FixedFlyerReader(params FlyerReadProduct[] items) : IFlyerReader
{
    public Task<Result<FlyerReading>> ReadAsync(IReadOnlyList<ClaudeImage> images, IReadOnlyList<Allergen> allergens, CancellationToken cancellationToken) =>
        Task.FromResult(Result<FlyerReading>.Ok(new FlyerReading(items)));
}

/// <summary>決まった検証の結果を返す</summary>
internal sealed class FixedFlyerReviewer(FlyerReview? review = null) : IFlyerReviewer
{
    /// <summary>検証に渡された読み取り結果</summary>
    public FlyerReading? Received { get; private set; }

    public Task<Result<FlyerReview>> ReviewAsync(IReadOnlyList<ClaudeImage> images, FlyerReading reading, CancellationToken cancellationToken)
    {
        Received = reading;
        return Task.FromResult(review is null ? Result<FlyerReview>.Error("検証できません") : Result<FlyerReview>.Ok(review));
    }
}

/// <summary>進捗を捨てる</summary>
internal sealed class NullProgress : IJobProgress
{
    /// <summary>最後に報告された返答の文章</summary>
    public string Text { get; private set; } = string.Empty;

    public void Report(string stage)
    {
    }

    public void ReportText(string text) => Text = text;
}

/// <summary>決まった時刻</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
