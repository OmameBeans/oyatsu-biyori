using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OyatsuBiyori.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuAgentSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentSessionFingerprint",
                table: "menus",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentSessionId",
                table: "menus",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentSessionFingerprint",
                table: "menus");

            migrationBuilder.DropColumn(
                name: "AgentSessionId",
                table: "menus");
        }
    }
}
