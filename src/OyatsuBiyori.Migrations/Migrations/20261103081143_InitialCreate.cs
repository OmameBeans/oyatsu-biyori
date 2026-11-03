using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OyatsuBiyori.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "allergen_groups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergen_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "allergens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    LabelNotGuaranteed = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "api_usage_logs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CalledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheCreationInputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheReadInputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_usage_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menus",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    WeeklyBudget = table.Column<int>(type: "INTEGER", nullable: false),
                    ListedProducts = table.Column<string>(type: "TEXT", nullable: false),
                    ExcludedProductIds = table.Column<string>(type: "TEXT", nullable: false),
                    RequestText = table.Column<string>(type: "TEXT", nullable: true),
                    InterpretedRequests = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true, defaultValueSql: "randomblob(8)"),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    JanCode = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    Maker = table.Column<string>(type: "TEXT", nullable: true),
                    Volume = table.Column<string>(type: "TEXT", nullable: true),
                    PiecesPerPack = table.Column<int>(type: "INTEGER", nullable: true),
                    DefaultPerChildCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SnackType = table.Column<string>(type: "TEXT", nullable: false),
                    ListedWeekStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Price = table.Column<int>(type: "INTEGER", nullable: true),
                    PriceExcludingTax = table.Column<int>(type: "INTEGER", nullable: true),
                    OrderNo = table.Column<string>(type: "TEXT", nullable: true),
                    TargetAgeLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "safety_setting_logs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OperatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OperatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenId = table.Column<string>(type: "TEXT", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_safety_setting_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultChildrenCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ServingWeekdays = table.Column<string>(type: "TEXT", nullable: false),
                    RecheckDays = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateAvoidDays = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultWeeklyBudget = table.Column<int>(type: "INTEGER", nullable: true),
                    DuplicateRuleNote = table.Column<string>(type: "TEXT", nullable: true),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "allergen_group_members",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenGroupId = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenId = table.Column<string>(type: "TEXT", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergen_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allergen_group_members_allergen_groups_AllergenGroupId",
                        column: x => x.AllergenGroupId,
                        principalTable: "allergen_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_allergen_group_members_allergens_AllergenId",
                        column: x => x.AllergenId,
                        principalTable: "allergens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "allergen_keywords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenId = table.Column<string>(type: "TEXT", nullable: false),
                    Keyword = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedKeyword = table.Column<string>(type: "TEXT", nullable: false),
                    IsSeed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergen_keywords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allergen_keywords_allergens_AllergenId",
                        column: x => x.AllergenId,
                        principalTable: "allergens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "avoided_allergens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenId = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AddedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avoided_allergens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_avoided_allergens_allergens_AllergenId",
                        column: x => x.AllergenId,
                        principalTable: "allergens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_days",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MenuId = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ChildrenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChildrenCountSource = table.Column<string>(type: "TEXT", nullable: false),
                    DayType = table.Column<string>(type: "TEXT", nullable: false),
                    DayTypeEdited = table.Column<bool>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    LabelEdited = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_days", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_days_menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_messages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MenuId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_messages_menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "allergen_checks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    SourceNote = table.Column<string>(type: "TEXT", nullable: true),
                    RawIngredientsOcr = table.Column<string>(type: "TEXT", nullable: true),
                    RawIngredients = table.Column<string>(type: "TEXT", nullable: true),
                    ContaminationNote = table.Column<string>(type: "TEXT", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CheckedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergen_checks", x => x.Id);
                    table.CheckConstraint("CK_allergen_checks_checked_by", "length(trim(CheckedBy)) > 0");
                    table.CheckConstraint("CK_allergen_checks_confirmed_requires_package", "Status <> 'Confirmed' OR (Source = 'Package' AND RawIngredients IS NOT NULL AND length(trim(RawIngredients)) > 0)");
                    table.ForeignKey(
                        name: "FK_allergen_checks_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_tags_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_items",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MenuDayId = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", nullable: false),
                    UnitPrice = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderNo = table.Column<string>(type: "TEXT", nullable: true),
                    PerChildCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PackCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcknowledgedJudgement = table.Column<string>(type: "TEXT", nullable: true),
                    AcknowledgedJudgementFingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_items_menu_days_MenuDayId",
                        column: x => x.MenuDayId,
                        principalTable: "menu_days",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_menu_items_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "allergen_check_items",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenCheckId = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenId = table.Column<string>(type: "TEXT", nullable: false),
                    Contains = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergen_check_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allergen_check_items_allergen_checks_AllergenCheckId",
                        column: x => x.AllergenCheckId,
                        principalTable: "allergen_checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allergen_check_items_allergens_AllergenId",
                        column: x => x.AllergenId,
                        principalTable: "allergens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "allergen_groups",
                columns: new[] { "Id", "Name", "SortOrder", "UpdateDateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { "17715af1-f6d6-5738-bb41-30d216a4dbe4", "ナッツ類（すべて）", 10, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "84786026-c3c2-53cb-99d6-2ee68495689d", "甲殻類", 20, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                table: "allergens",
                columns: new[] { "Id", "Category", "Code", "LabelNotGuaranteed", "Name", "SortOrder", "UpdateDateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { "08d08946-01e3-5701-a222-444b0e035651", "Recommended", "pork", true, "豚肉", 250, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "0f95f128-815e-5682-910a-0622d65ae7ce", "Recommended", "salmon_roe", true, "いくら", 130, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "0fe7fdc2-ff60-56f8-a099-e7822aecf133", "Specified", "crab", false, "かに", 20, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "14082cf5-c939-5ba4-b68c-8a93eed790a1", "Recommended", "soybean", true, "大豆", 210, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "15468251-223d-589b-8f2c-c5bd3f6e8ea8", "FacilityAdded", "pecan", false, "ペカンナッツ", 310, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "1654c78b-09f8-5d06-bcc4-6c2fbee54fdb", "Recommended", "orange", true, "オレンジ", 140, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "1a6704b3-6923-577c-a3e3-25f4dc058700", "Recommended", "gelatin", true, "ゼラチン", 200, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", "FacilityAdded", "hazelnut", false, "ヘーゼルナッツ", 300, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "22b1fa43-b1c2-5a21-8a68-005e9e8f56e0", "FacilityAdded", "brazil_nut", false, "ブラジルナッツ", 320, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "2ad0d7cf-4b00-52a6-b63f-dcf475d37ae8", "Specified", "cashew", true, "カシューナッツ", 90, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "3876c10c-ec68-5dc2-89bb-b79bbdeef09d", "Recommended", "apple", true, "りんご", 290, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "403f3d51-956c-5848-b7f3-411324ae00f7", "Specified", "wheat", false, "小麦", 40, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "43bc57fb-6486-574e-8d89-b2e7768d06e6", "Recommended", "beef", true, "牛肉", 160, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "4a0055f7-67a7-5392-a415-a99df017169c", "Recommended", "mackerel", true, "さば", 190, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "5810e666-138d-5ac7-97bd-e4819d47d031", "Recommended", "peach", true, "もも", 270, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "588c4d0d-3329-5c36-b438-72a059d59f03", "Recommended", "macadamia", true, "マカダミアナッツ", 260, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "67a65d9c-8b2a-598e-85d8-891be56e9ce7", "Specified", "peanut", false, "落花生（ピーナッツ）", 80, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "82f111cc-36f5-5a10-8913-dd32fdd88de5", "Recommended", "pistachio", true, "ピスタチオ", 240, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "91d37ad7-241b-5e09-99b2-43d10521bacb", "FacilityAdded", "coconut", false, "ココナッツ", 330, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "92764a68-823b-546c-96f8-d329efce1a06", "Recommended", "abalone", true, "あわび", 110, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "9e7f46c6-8ff2-5df8-9fd0-0ed82f1b403a", "Recommended", "yam", true, "やまいも", 280, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "a8c6b740-dc83-5039-ae1c-1363572c3777", "Specified", "buckwheat", false, "そば", 50, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "ad18463d-40d6-5572-8b7d-fb47eb930bdb", "FacilityAdded", "pine_nut", false, "松の実", 340, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "b3a265e4-986a-507b-bf5c-3b701175c48a", "Recommended", "kiwifruit", true, "キウイフルーツ", 150, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "b3fbf25a-7145-59a9-8d18-8c756fc2342d", "Recommended", "chicken", true, "鶏肉", 220, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "b794ad09-33c2-54bd-97cd-b5f5c84499e3", "Recommended", "banana", true, "バナナ", 230, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "c1c38fe3-d70a-59e6-a033-f8c0d3de5ca0", "Specified", "milk", false, "乳", 70, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "c85b487c-c03b-59a7-a754-716763383de2", "Recommended", "almond", true, "アーモンド", 100, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "cac5c06c-3692-53f1-9a39-1b27cb6ef16f", "Specified", "walnut", false, "くるみ", 30, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "e273cd5a-80cc-5159-88ac-096294013325", "Recommended", "salmon", true, "さけ", 180, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "e28f5971-05ba-5e43-baf3-4184e57d20ac", "Recommended", "sesame", true, "ごま", 170, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "e85f35c4-1fe3-57de-b9ed-851e18759606", "Recommended", "squid", true, "いか", 120, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "ebb177df-066c-5715-b89f-ca457e02448c", "Specified", "egg", false, "卵", 60, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "f15f03dd-1e21-5c81-8d08-9aa2bda54afb", "Specified", "shrimp", false, "えび", 10, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "Id", "DefaultChildrenCount", "DefaultWeeklyBudget", "DuplicateAvoidDays", "DuplicateRuleNote", "RecheckDays", "ServingWeekdays", "UpdateDateTime", "UpdateUserId" },
                values: new object[] { "aeea1fb9-3db5-5748-a3db-1b20703d97e9", null, null, "[{\"snackType\":\"Salty\",\"days\":30},{\"snackType\":\"Sweet\",\"days\":30}]", null, 90, "[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\"]", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.InsertData(
                table: "allergen_group_members",
                columns: new[] { "Id", "AllergenGroupId", "AllergenId", "UpdateDateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { "21f8185c-7200-58c7-878d-7ef79b8edc7e", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "67a65d9c-8b2a-598e-85d8-891be56e9ce7", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "253a1f19-7529-5b8d-98a9-25f24757637c", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "c85b487c-c03b-59a7-a754-716763383de2", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "33b13a39-263b-5aba-b970-43dd2b9da390", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "588c4d0d-3329-5c36-b438-72a059d59f03", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "3fcdad65-db5b-57a4-be9e-f58fefe5d871", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "cac5c06c-3692-53f1-9a39-1b27cb6ef16f", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "51253b0c-398d-5677-8ab3-096f48c5c96f", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "55df698a-0322-537d-9b3f-4cb8fe7a160d", "84786026-c3c2-53cb-99d6-2ee68495689d", "0fe7fdc2-ff60-56f8-a099-e7822aecf133", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "5c0af8e1-b2fb-5a44-844b-ea686afd0ce6", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "22b1fa43-b1c2-5a21-8a68-005e9e8f56e0", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "6608b8c3-321c-5993-a8f8-7015677acb7d", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "ad18463d-40d6-5572-8b7d-fb47eb930bdb", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "8883d18b-fb99-568f-aeee-a049fbe4bbd3", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "82f111cc-36f5-5a10-8913-dd32fdd88de5", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "c753c4ad-7886-534e-8558-8197414f2a5b", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "2ad0d7cf-4b00-52a6-b63f-dcf475d37ae8", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "c8096668-3fb1-5d6c-8bf1-3e6bbf54daa2", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "15468251-223d-589b-8f2c-c5bd3f6e8ea8", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "e1055282-817c-5617-8e78-584c0609040e", "17715af1-f6d6-5738-bb41-30d216a4dbe4", "91d37ad7-241b-5e09-99b2-43d10521bacb", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "ed29904e-501e-5c9a-8331-7d18c7570439", "84786026-c3c2-53cb-99d6-2ee68495689d", "f15f03dd-1e21-5c81-8d08-9aa2bda54afb", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                table: "allergen_keywords",
                columns: new[] { "Id", "AllergenId", "IsSeed", "Keyword", "NormalizedKeyword", "UpdateDateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { "0388656a-b1f9-5a96-8453-51637a4a4ef6", "cac5c06c-3692-53f1-9a39-1b27cb6ef16f", true, "ウォールナッツ", "うぉーるなっつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "0f0916f5-65ee-5c9b-8c21-01fb4f69404a", "cac5c06c-3692-53f1-9a39-1b27cb6ef16f", true, "胡桃", "胡桃", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "0fc5b56e-d6a3-5415-99b8-edb17aadd357", "c85b487c-c03b-59a7-a754-716763383de2", true, "プラリネ", "ぷらりね", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "115dee58-b131-58a3-9193-033d7e095dc7", "91d37ad7-241b-5e09-99b2-43d10521bacb", true, "ココナッツ", "ここなっつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "163c3790-e38a-5c49-94d2-e2d2de8336d0", "ad18463d-40d6-5572-8b7d-fb47eb930bdb", true, "パインナッツ", "ぱいんなっつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "1dd82404-1e1b-5cce-aff7-ca6b9a74de31", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "ノワゼット", "のわぜっと", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "2dd0cd34-b22e-5cc7-9b58-73f7f61e42db", "67a65d9c-8b2a-598e-85d8-891be56e9ce7", true, "南京豆", "南京豆", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "3315fa62-17fd-5779-a96a-a85979e03aec", "82f111cc-36f5-5a10-8913-dd32fdd88de5", true, "ピスタチオ", "ぴすたちお", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "33c41737-9bd0-54eb-acc6-31342b5b54f2", "15468251-223d-589b-8f2c-c5bd3f6e8ea8", true, "ペカン", "ぺかん", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "40cff666-4d9e-5ca8-9bdd-af0cdd5961a1", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "ヘーゼル", "へーぜる", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "49a81325-144a-5849-8306-7754c3418b9a", "91d37ad7-241b-5e09-99b2-43d10521bacb", true, "ココナツ", "ここなつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "5703d9c3-f874-5105-abba-d44e94707bba", "2ad0d7cf-4b00-52a6-b63f-dcf475d37ae8", true, "カシューナッツ", "かしゅーなっつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "5bc1d956-f917-5195-9cd1-1aa0ed2bb659", "91d37ad7-241b-5e09-99b2-43d10521bacb", true, "ナタデココ", "なたでここ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "63ad0b8e-d948-5ef2-9da9-e39e4673efe4", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "ジャンドゥーヤ", "じゃんどぅーや", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "6f24ec82-b935-5bc7-9ee5-b56c7158648d", "588c4d0d-3329-5c36-b438-72a059d59f03", true, "マカデミア", "まかでみあ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "734ce41c-a11f-5f8a-a9d6-f3b1d922cd05", "67a65d9c-8b2a-598e-85d8-891be56e9ce7", true, "落花生", "落花生", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "7dfd5e0f-4531-5d15-848c-937ba2cf5037", "15468251-223d-589b-8f2c-c5bd3f6e8ea8", true, "ピーカン", "ぴーかん", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "7f501c87-9c89-5a62-b6c4-d3f3062b10fc", "588c4d0d-3329-5c36-b438-72a059d59f03", true, "マカダミア", "まかだみあ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "7fa9a5a5-3306-5ecd-8c38-e2baed8e3758", "67a65d9c-8b2a-598e-85d8-891be56e9ce7", true, "ピーナッツ", "ぴーなっつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "8f6a239c-877d-5251-a294-8710b42a1393", "c85b487c-c03b-59a7-a754-716763383de2", true, "フロランタン", "ふろらんたん", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "915b5daf-e21d-56d7-becc-ddcf12f3ba4a", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "ノアゼット", "のあぜっと", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "931f2db8-8b89-5cf1-ba5f-0e83f246f12f", "22b1fa43-b1c2-5a21-8a68-005e9e8f56e0", true, "ブラジルナッツ", "ぶらじるなっつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "9db35f31-34f2-5121-b5c6-3a7dd194dbe0", "cac5c06c-3692-53f1-9a39-1b27cb6ef16f", true, "くるみ", "くるみ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "9ea2711d-dc50-580b-a4f5-39e04b9e66a0", "ad18463d-40d6-5572-8b7d-fb47eb930bdb", true, "松のみ", "松のみ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "9ff49bf0-1028-5fdc-a701-e937076bba8f", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "ヘイゼル", "へいぜる", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "a0cdb1e7-a573-5bdf-8085-dcbeb1acca76", "ad18463d-40d6-5572-8b7d-fb47eb930bdb", true, "松の実", "松の実", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "b1912269-4099-573a-94be-e3fce843f189", "c85b487c-c03b-59a7-a754-716763383de2", true, "アーモンドプードル", "あーもんどぷーどる", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "ba103364-df65-542b-a9d8-6de037079d03", "c85b487c-c03b-59a7-a754-716763383de2", true, "マカロン", "まかろん", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "c0b64a15-de63-51b7-8693-9d28ce78d8bf", "c85b487c-c03b-59a7-a754-716763383de2", true, "アーモンド", "あーもんど", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "c685f0e8-8b43-5137-a394-0e84f917b2fe", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "ジャンドゥ", "じゃんどぅ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "d0e1a550-a3a1-5265-98f9-031b5f6c6640", "67a65d9c-8b2a-598e-85d8-891be56e9ce7", true, "ピーナツ", "ぴーなつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "d29c1218-924c-5eb3-9fa0-a01285f3c960", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "ヘーゼルナッツ", "へーぜるなっつ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "d4835840-358e-51c4-b237-5d908fbc136d", "2ad0d7cf-4b00-52a6-b63f-dcf475d37ae8", true, "カシュー", "かしゅー", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "da9b0fff-15e1-5dcf-8625-f906983a37d9", "1f61f051-d057-5edb-b9b8-e4f6eeef3af8", true, "プラリネ", "ぷらりね", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "de1f88e0-0a5b-5c9c-9d62-eae5a902b45a", "c85b487c-c03b-59a7-a754-716763383de2", true, "マジパン", "まじぱん", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_allergen_check_items_AllergenCheckId_AllergenId",
                table: "allergen_check_items",
                columns: new[] { "AllergenCheckId", "AllergenId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_allergen_check_items_AllergenId",
                table: "allergen_check_items",
                column: "AllergenId");

            migrationBuilder.CreateIndex(
                name: "IX_allergen_checks_ProductId_CheckedAt",
                table: "allergen_checks",
                columns: new[] { "ProductId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_allergen_group_members_AllergenGroupId_AllergenId",
                table: "allergen_group_members",
                columns: new[] { "AllergenGroupId", "AllergenId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_allergen_group_members_AllergenId",
                table: "allergen_group_members",
                column: "AllergenId");

            migrationBuilder.CreateIndex(
                name: "IX_allergen_keywords_AllergenId_NormalizedKeyword",
                table: "allergen_keywords",
                columns: new[] { "AllergenId", "NormalizedKeyword" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_allergens_Code",
                table: "allergens",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_usage_logs_CalledAt",
                table: "api_usage_logs",
                column: "CalledAt");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_avoided_allergens_AllergenId",
                table: "avoided_allergens",
                column: "AllergenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menu_days_MenuId_Date",
                table: "menu_days",
                columns: new[] { "MenuId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_MenuDayId",
                table: "menu_items",
                column: "MenuDayId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_ProductId",
                table: "menu_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_messages_MenuId_CreatedAt",
                table: "menu_messages",
                columns: new[] { "MenuId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_menus_WeekStart",
                table: "menus",
                column: "WeekStart",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_tags_ProductId",
                table: "product_tags",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_products_JanCode",
                table: "products",
                column: "JanCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_NormalizedName",
                table: "products",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_safety_setting_logs_OperatedAt",
                table: "safety_setting_logs",
                column: "OperatedAt");

            // 献立を更新したら row_version を振り直す（同時に保存したときの衝突を見つけるため。D-46）
            migrationBuilder.Sql(
                """
                CREATE TRIGGER menus_row_version_update
                AFTER UPDATE ON menus
                BEGIN
                    UPDATE menus SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                END;
                """);

            // アレルゲン確認の記録は追加のみ。DB の側でも書き換え・削除を止める（SR-07）
            migrationBuilder.Sql("CREATE TRIGGER allergen_checks_no_update BEFORE UPDATE ON allergen_checks BEGIN SELECT RAISE(ABORT, 'allergen_checks は追加のみです'); END;");
            migrationBuilder.Sql("CREATE TRIGGER allergen_checks_no_delete BEFORE DELETE ON allergen_checks BEGIN SELECT RAISE(ABORT, 'allergen_checks は追加のみです'); END;");
            migrationBuilder.Sql("CREATE TRIGGER allergen_check_items_no_update BEFORE UPDATE ON allergen_check_items BEGIN SELECT RAISE(ABORT, 'allergen_check_items は追加のみです'); END;");
            migrationBuilder.Sql("CREATE TRIGGER allergen_check_items_no_delete BEFORE DELETE ON allergen_check_items BEGIN SELECT RAISE(ABORT, 'allergen_check_items は追加のみです'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS menus_row_version_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS allergen_checks_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS allergen_checks_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS allergen_check_items_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS allergen_check_items_no_delete;");

            migrationBuilder.DropTable(
                name: "allergen_check_items");

            migrationBuilder.DropTable(
                name: "allergen_group_members");

            migrationBuilder.DropTable(
                name: "allergen_keywords");

            migrationBuilder.DropTable(
                name: "api_usage_logs");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "avoided_allergens");

            migrationBuilder.DropTable(
                name: "menu_items");

            migrationBuilder.DropTable(
                name: "menu_messages");

            migrationBuilder.DropTable(
                name: "product_tags");

            migrationBuilder.DropTable(
                name: "safety_setting_logs");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "allergen_checks");

            migrationBuilder.DropTable(
                name: "allergen_groups");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "allergens");

            migrationBuilder.DropTable(
                name: "menu_days");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "menus");
        }
    }
}
