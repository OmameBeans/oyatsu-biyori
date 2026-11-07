using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OyatsuBiyori.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCashewLabelAbbreviation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "allergen_label_abbreviations",
                columns: new[] { "Id", "Abbreviation", "AllergenId", "NormalizedAbbreviation", "UpdateDateTime", "UpdateUserId" },
                values: new object[] { "4ac2a530-431b-5bc3-b5f6-a6068211f532", "カ", "2ad0d7cf-4b00-52a6-b63f-dcf475d37ae8", "カ", new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "allergen_label_abbreviations",
                keyColumn: "Id",
                keyValue: "4ac2a530-431b-5bc3-b5f6-a6068211f532");
        }
    }
}
