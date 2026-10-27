using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveLink.Data.Migrations
{
    /// <inheritdoc />
    public partial class WeeklyRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeeklyRewards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Target = table.Column<int>(type: "int", nullable: false),
                    Achieved = table.Column<bool>(type: "bit", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyRewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyRewards_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyRewards_MemberId_WeekStart",
                table: "WeeklyRewards",
                columns: new[] { "MemberId", "WeekStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyRewards_WeekStart",
                table: "WeeklyRewards",
                column: "WeekStart");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeeklyRewards");
        }
    }
}
