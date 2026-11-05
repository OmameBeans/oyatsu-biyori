using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OyatsuBiyori.Migrations.Migrations
{
    /// <summary>
    /// 日の種類から「手作り」（Handmade）をなくす。列の形は変えず、残っている「手作り」の日を「おやつなし」にする
    /// （手作りの日は金額0・商品を出さない扱いで、おやつなしと同じ）。
    /// </summary>
    public partial class RemoveHandmadeDayType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE menu_days SET DayType = 'NoSnack' WHERE DayType = 'Handmade';");

            // 「要望をこう理解しました」の JSON にも日の種類が入っている（読み込めなくならないように直す）
            migrationBuilder.Sql(
                "UPDATE menus SET InterpretedRequests = REPLACE(InterpretedRequests, '\"dayType\":\"Handmade\"', '\"dayType\":\"NoSnack\"') "
                + "WHERE InterpretedRequests LIKE '%\"Handmade\"%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // どの日が「手作り」だったかは残っていないため、戻さない
        }
    }
}
