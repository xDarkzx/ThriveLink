using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveLink.Data.Migrations
{
    /// <inheritdoc />
    public partial class PartnerHealthChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthCheckRejects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CheckDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Measure = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Partner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthCheckRejects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealthCheckResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    CheckDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Measure = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Partner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthCheckResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealthCheckResults_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealthCheckResults_MemberId_CheckDate_Measure",
                table: "HealthCheckResults",
                columns: new[] { "MemberId", "CheckDate", "Measure" },
                unique: true);

            // Hand-written: staging table loaded by Data Factory, and the procedure that applies it.
            // EXEC() because the idempotent migration script wraps each migration in IF/BEGIN/END,
            // where CREATE SCHEMA and CREATE PROCEDURE are not allowed directly.
            migrationBuilder.Sql("EXEC(N'CREATE SCHEMA staging')");
            migrationBuilder.Sql(
                """
                CREATE TABLE staging.HealthCheckImport (
                    MemberNumber nvarchar(50) NULL,
                    CheckDate nvarchar(50) NULL,
                    Measure nvarchar(50) NULL,
                    Partner nvarchar(100) NULL
                )
                """);
            migrationBuilder.Sql(
                """
                EXEC(N'
                CREATE OR ALTER PROCEDURE dbo.usp_ApplyHealthChecks
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DECLARE @now datetimeoffset = SYSDATETIMEOFFSET();
                    DECLARE @today date = CAST(SYSUTCDATETIME() AS date);

                    -- Normalise once: trim member numbers, parse ISO dates (yyyy-MM-dd).
                    SELECT LTRIM(RTRIM(s.MemberNumber)) AS MemberNumber, s.CheckDate AS RawDate,
                           TRY_CONVERT(date, s.CheckDate, 23) AS CheckDate,
                           LTRIM(RTRIM(s.Measure)) AS Measure, ISNULL(NULLIF(LTRIM(RTRIM(s.Partner)), ''''), ''Unknown'') AS Partner,
                           m.Id AS MemberId
                    INTO #rows
                    FROM staging.HealthCheckImport s
                    LEFT JOIN dbo.Members m ON m.MemberNumber = LTRIM(RTRIM(s.MemberNumber));

                    -- Keep anything we cannot apply, with a reason, so support can follow up with the partner.
                    INSERT INTO dbo.HealthCheckRejects (MemberNumber, CheckDate, Measure, Partner, Reason, RejectedAt)
                    SELECT LEFT(MemberNumber, 50), LEFT(RawDate, 50), LEFT(Measure, 50), LEFT(Partner, 100),
                           CASE WHEN MemberId IS NULL THEN ''Unknown member''
                                WHEN CheckDate IS NULL THEN ''Invalid date''
                                WHEN CheckDate > @today THEN ''Date in the future''
                                ELSE ''Unknown measure'' END,
                           @now
                    FROM #rows
                    WHERE MemberId IS NULL OR CheckDate IS NULL OR CheckDate > @today
                       OR Measure NOT IN (''BMI'', ''BloodPressure'', ''Glucose'', ''Cholesterol'');

                    -- New results only: re-sending the same file is harmless.
                    INSERT INTO dbo.HealthCheckResults (MemberId, CheckDate, Measure, Partner, ImportedAt)
                    SELECT r.MemberId, r.CheckDate, r.Measure, MIN(r.Partner), @now
                    FROM #rows r
                    WHERE r.MemberId IS NOT NULL AND r.CheckDate IS NOT NULL AND r.CheckDate <= @today
                      AND r.Measure IN (''BMI'', ''BloodPressure'', ''Glucose'', ''Cholesterol'')
                      AND NOT EXISTS (SELECT 1 FROM dbo.HealthCheckResults h
                                      WHERE h.MemberId = r.MemberId AND h.CheckDate = r.CheckDate AND h.Measure = r.Measure)
                    GROUP BY r.MemberId, r.CheckDate, r.Measure;

                    -- 1,000 points per measure, one ledger row per member and check date.
                    MERGE dbo.PointsEntries AS t
                    USING (SELECT h.MemberId, h.CheckDate, COUNT(*) * 1000 AS Points
                           FROM dbo.HealthCheckResults h
                           WHERE h.MemberId IN (SELECT MemberId FROM #rows WHERE MemberId IS NOT NULL)
                           GROUP BY h.MemberId, h.CheckDate) AS s
                    ON t.MemberId = s.MemberId AND t.[Date] = s.CheckDate AND t.Category = ''HealthCheck''
                    WHEN MATCHED AND t.Points <> s.Points THEN UPDATE SET Points = s.Points
                    WHEN NOT MATCHED THEN INSERT (MemberId, [Date], Category, Points)
                                          VALUES (s.MemberId, s.CheckDate, ''HealthCheck'', s.Points);

                    DELETE FROM staging.HealthCheckImport;
                END')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_ApplyHealthChecks");
            migrationBuilder.Sql("DROP TABLE IF EXISTS staging.HealthCheckImport");
            migrationBuilder.Sql("EXEC(N'DROP SCHEMA IF EXISTS staging')");

            migrationBuilder.DropTable(
                name: "HealthCheckRejects");

            migrationBuilder.DropTable(
                name: "HealthCheckResults");
        }
    }
}
