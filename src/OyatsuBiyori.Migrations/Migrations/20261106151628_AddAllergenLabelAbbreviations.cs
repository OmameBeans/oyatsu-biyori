using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OyatsuBiyori.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAllergenLabelAbbreviations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "allergen_label_abbreviations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AllergenId = table.Column<string>(type: "TEXT", nullable: false),
                    Abbreviation = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedAbbreviation = table.Column<string>(type: "TEXT", nullable: false),
                    UpdateDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergen_label_abbreviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allergen_label_abbreviations_allergens_AllergenId",
                        column: x => x.AllergenId,
                        principalTable: "allergens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "allergen_label_abbreviations",
                columns: new[] { "Id", "Abbreviation", "AllergenId", "NormalizedAbbreviation", "UpdateDateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { "6dc8cd42-a464-5cce-98c5-4691ba1b729c", "麦", "403f3d51-956c-5848-b7f3-411324ae00f7", "麦", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "7c89cb49-90dc-5c13-81da-956a121446a9", "そ", "a8c6b740-dc83-5039-ae1c-1363572c3777", "そ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "87ef7b09-f621-5614-8206-0accdaea59f3", "落", "67a65d9c-8b2a-598e-85d8-891be56e9ce7", "落", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "932cf2c8-df47-52dd-a96c-37bd00cdaf5d", "か", "0fe7fdc2-ff60-56f8-a099-e7822aecf133", "か", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "b444e0b4-6947-5d1c-943a-22322b3c7299", "く", "cac5c06c-3692-53f1-9a39-1b27cb6ef16f", "く", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "e31068d1-20ad-5c15-9a92-1ec4cf6562fc", "え", "f15f03dd-1e21-5c81-8d08-9aa2bda54afb", "え", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_allergen_label_abbreviations_AllergenId_NormalizedAbbreviation",
                table: "allergen_label_abbreviations",
                columns: new[] { "AllergenId", "NormalizedAbbreviation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allergen_label_abbreviations");
        }
    }
}
