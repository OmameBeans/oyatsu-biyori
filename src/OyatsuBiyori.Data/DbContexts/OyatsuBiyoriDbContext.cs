using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.MasterData;

namespace OyatsuBiyori.Data.DbContexts;

/// <summary>
/// おやつ日和の DbContext（SQLite）。テーブル名は 04 データ要件に合わせる。
/// </summary>
public sealed class OyatsuBiyoriDbContext(DbContextOptions<OyatsuBiyoriDbContext> options) : IdentityDbContext<AppUser>(options)
{
    /// <summary>献立の row_version を更新するトリガーの名前（D-46）</summary>
    public const string MenuRowVersionTrigger = "menus_row_version_update";

    /// <summary>確認の記録の書き換え・削除を止めるトリガーの名前（SR-07）</summary>
    public const string AllergenChecksNoUpdateTrigger = "allergen_checks_no_update";

    public const string AllergenChecksNoDeleteTrigger = "allergen_checks_no_delete";

    public const string AllergenCheckItemsNoUpdateTrigger = "allergen_check_items_no_update";

    public const string AllergenCheckItemsNoDeleteTrigger = "allergen_check_items_no_delete";

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<Allergen> Allergens => Set<Allergen>();

    public DbSet<AllergenKeyword> AllergenKeywords => Set<AllergenKeyword>();

    public DbSet<AllergenGroup> AllergenGroups => Set<AllergenGroup>();

    public DbSet<AllergenGroupMember> AllergenGroupMembers => Set<AllergenGroupMember>();

    public DbSet<AvoidedAllergen> AvoidedAllergens => Set<AvoidedAllergen>();

    public DbSet<SafetySettingLog> SafetySettingLogs => Set<SafetySettingLog>();

    public DbSet<ApiUsageLog> ApiUsageLogs => Set<ApiUsageLog>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductTag> ProductTags => Set<ProductTag>();

    public DbSet<AllergenCheck> AllergenChecks => Set<AllergenCheck>();

    public DbSet<AllergenCheckItem> AllergenCheckItems => Set<AllergenCheckItem>();

    public DbSet<Menu> Menus => Set<Menu>();

    public DbSet<MenuDay> MenuDays => Set<MenuDay>();

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    public DbSet<MenuMessage> MenuMessages => Set<MenuMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 日時はすべて UTC として読み書きする
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // enum は読みやすいように文字列で保存する
        configurationBuilder.Properties<AllergenCategory>().HaveConversion<string>();
        configurationBuilder.Properties<AllergenCheckStatus>().HaveConversion<string>();
        configurationBuilder.Properties<AllergenCheckSource>().HaveConversion<string>();
        configurationBuilder.Properties<AllergenJudgementKind>().HaveConversion<string>();
        configurationBuilder.Properties<SnackType>().HaveConversion<string>();
        configurationBuilder.Properties<ProductTagKind>().HaveConversion<string>();
        configurationBuilder.Properties<ProductTagSource>().HaveConversion<string>();
        configurationBuilder.Properties<MenuStatus>().HaveConversion<string>();
        configurationBuilder.Properties<DayType>().HaveConversion<string>();
        configurationBuilder.Properties<ChildrenCountSource>().HaveConversion<string>();
        configurationBuilder.Properties<MenuMessageRole>().HaveConversion<string>();
        configurationBuilder.Properties<SafetySettingLogKind>().HaveConversion<string>();
        configurationBuilder.Properties<ClaudePurpose>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Setting>(entity =>
        {
            entity.ToTable("settings");
            entity.Property(e => e.ServingWeekdays)
                .HasConversion(JsonColumn.Converter<List<DayOfWeek>>(), JsonColumn.Comparer<List<DayOfWeek>>());
            entity.Property(e => e.DuplicateAvoidDays)
                .HasConversion(JsonColumn.Converter<List<DuplicateAvoidRule>>(), JsonColumn.Comparer<List<DuplicateAvoidRule>>());
            entity.HasData(MasterSeed.Setting());
        });

        builder.Entity<Allergen>(entity =>
        {
            entity.ToTable("allergens");
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasMany(e => e.Keywords).WithOne(e => e.Allergen).OnDelete(DeleteBehavior.Cascade);
            entity.HasData(MasterSeed.Allergens());
        });

        builder.Entity<AllergenKeyword>(entity =>
        {
            entity.ToTable("allergen_keywords");
            entity.HasIndex(e => new { e.AllergenId, e.NormalizedKeyword }).IsUnique();
            entity.HasData(MasterSeed.Keywords());
        });

        builder.Entity<AllergenGroup>(entity =>
        {
            entity.ToTable("allergen_groups");
            entity.HasMany(e => e.Members).WithOne(e => e.AllergenGroup).OnDelete(DeleteBehavior.Cascade);
            entity.HasData(MasterSeed.Groups());
        });

        builder.Entity<AllergenGroupMember>(entity =>
        {
            entity.ToTable("allergen_group_members");
            entity.HasIndex(e => new { e.AllergenGroupId, e.AllergenId }).IsUnique();

            // グループに入っている品目は削除できない（D-56）
            entity.HasOne(e => e.Allergen).WithMany().OnDelete(DeleteBehavior.Restrict);
            entity.HasData(MasterSeed.GroupMembers());
        });

        builder.Entity<AvoidedAllergen>(entity =>
        {
            entity.ToTable("avoided_allergens");
            entity.HasIndex(e => e.AllergenId).IsUnique();

            // 避ける品目に入っている品目は削除できない（D-56）
            entity.HasOne(e => e.Allergen).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SafetySettingLog>(entity =>
        {
            // allergen_id は外部キーにしない（品目を削除しても記録は残す。D-56・D-72）
            entity.ToTable("safety_setting_logs");
            entity.HasIndex(e => e.OperatedAt);
        });

        builder.Entity<ApiUsageLog>(entity =>
        {
            entity.ToTable("api_usage_logs");
            entity.HasIndex(e => e.CalledAt);
        });

        builder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasIndex(e => e.JanCode).IsUnique();
            entity.HasIndex(e => e.NormalizedName);
            entity.HasMany(e => e.Tags).WithOne(e => e.Product).OnDelete(DeleteBehavior.Cascade);

            // アレルゲン確認の記録は追加のみ。商品を消しても記録は消さない
            entity.HasMany(e => e.AllergenChecks).WithOne(e => e.Product).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductTag>(entity =>
        {
            entity.ToTable("product_tags");
        });

        builder.Entity<AllergenCheck>(entity =>
        {
            // 確認の記録は追加のみ（SR-07）。DB の側でも書き換え・削除をトリガーで止める
            // 「確認済み」は情報源がパッケージで、原材料の全文があるときだけ（SR-12・FR-PROD-06b）
            entity.ToTable("allergen_checks", table =>
            {
                table.HasTrigger(AllergenChecksNoUpdateTrigger);
                table.HasTrigger(AllergenChecksNoDeleteTrigger);
                table.HasCheckConstraint(
                    "CK_allergen_checks_confirmed_requires_package",
                    "Status <> 'Confirmed' OR (Source = 'Package' AND RawIngredients IS NOT NULL AND length(trim(RawIngredients)) > 0)");
                table.HasCheckConstraint("CK_allergen_checks_checked_by", "length(trim(CheckedBy)) > 0");
            });
            entity.HasIndex(e => new { e.ProductId, e.CheckedAt });
            entity.HasMany(e => e.Items).WithOne(e => e.AllergenCheck).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AllergenCheckItem>(entity =>
        {
            entity.ToTable("allergen_check_items", table =>
            {
                table.HasTrigger(AllergenCheckItemsNoUpdateTrigger);
                table.HasTrigger(AllergenCheckItemsNoDeleteTrigger);
            });
            entity.HasIndex(e => new { e.AllergenCheckId, e.AllergenId }).IsUnique();

            // 確認の記録に使われた品目は削除できない（D-56）
            entity.HasOne(e => e.Allergen).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Menu>(entity =>
        {
            // row_version は SQLite のトリガーで更新する。トリガーがあることを EF に伝える（D-46）
            entity.ToTable("menus", table => table.HasTrigger(MenuRowVersionTrigger));
            entity.HasIndex(e => e.WeekStart).IsUnique();
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .HasDefaultValueSql("randomblob(8)");
            entity.Property(e => e.ListedProducts)
                .HasConversion(JsonColumn.Converter<List<ListedProduct>>(), JsonColumn.Comparer<List<ListedProduct>>());
            entity.Property(e => e.ExcludedProductIds)
                .HasConversion(JsonColumn.Converter<List<string>>(), JsonColumn.Comparer<List<string>>());
            entity.Property(e => e.InterpretedRequests)
                .HasConversion(JsonColumn.Converter<InterpretedRequests?>(), JsonColumn.Comparer<InterpretedRequests?>());
            entity.HasMany(e => e.Days).WithOne(e => e.Menu).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Messages).WithOne(e => e.Menu).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MenuDay>(entity =>
        {
            entity.ToTable("menu_days");
            entity.HasIndex(e => new { e.MenuId, e.Date }).IsUnique();
            entity.HasMany(e => e.Items).WithOne(e => e.MenuDay).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MenuItem>(entity =>
        {
            entity.ToTable("menu_items");
            entity.HasOne(e => e.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MenuMessage>(entity =>
        {
            entity.ToTable("menu_messages");
            entity.HasIndex(e => new { e.MenuId, e.CreatedAt });
        });
    }
}
