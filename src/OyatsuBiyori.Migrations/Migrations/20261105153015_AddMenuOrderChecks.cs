using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OyatsuBiyori.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuOrderChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderChecks",
                table: "menus",
                type: "TEXT",
                nullable: false,
                // 既存の献立は「チェックなし」（空の JSON 配列）にする
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderChecks",
                table: "menus");
        }
    }
}
