using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduShelf.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhaseTwoProcessingSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnalyzedCharacters",
                table: "ResourceAnalyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "ResourceAnalyses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPartial",
                table: "ResourceAnalyses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedPages",
                table: "ResourceAnalyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceCharacters",
                table: "ResourceAnalyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SuggestedSubjectId",
                table: "ResourceAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuggestedTopicId",
                table: "ResourceAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalPages",
                table: "ResourceAnalyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PageBookmarks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageBookmarks", x => new { x.UserId, x.ResourceId });
                    table.CheckConstraint("CK_PageBookmarks_PageNumber", "[PageNumber] > 0");
                    table.ForeignKey(
                        name: "FK_PageBookmarks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PageBookmarks_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AnalyzeRequested = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotBeforeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.ResourceId);
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceEmbeddings",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Dimensions = table.Column<int>(type: "int", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VectorJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CoveredCharacters = table.Column<int>(type: "int", nullable: false),
                    IsPartial = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceEmbeddings", x => x.ResourceId);
                    table.ForeignKey(
                        name: "FK_ResourceEmbeddings_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceSearchDocuments",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    SearchText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceSearchDocuments", x => x.ResourceId);
                    table.ForeignKey(
                        name: "FK_ResourceSearchDocuments_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceTextExtractions",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExtractedFileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TotalPages = table.Column<int>(type: "int", nullable: false),
                    ProcessedPages = table.Column<int>(type: "int", nullable: false),
                    CharacterCount = table.Column<int>(type: "int", nullable: false),
                    IsTruncated = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExtractedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTextExtractions", x => x.ResourceId);
                    table.ForeignKey(
                        name: "FK_ResourceTextExtractions_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceTextPages",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTextPages", x => new { x.ResourceId, x.PageNumber });
                    table.ForeignKey(
                        name: "FK_ResourceTextPages_ResourceTextExtractions_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "ResourceTextExtractions",
                        principalColumn: "ResourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAnalyses_SuggestedSubjectId",
                table: "ResourceAnalyses",
                column: "SuggestedSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAnalyses_SuggestedTopicId",
                table: "ResourceAnalyses",
                column: "SuggestedTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_PageBookmarks_ResourceId",
                table: "PageBookmarks",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_Status_NotBeforeUtc",
                table: "ProcessingJobs",
                columns: new[] { "Status", "NotBeforeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceEmbeddings_Model_Dimensions",
                table: "ResourceEmbeddings",
                columns: new[] { "Model", "Dimensions" });

            migrationBuilder.AddForeignKey(
                name: "FK_ResourceAnalyses_Subjects_SuggestedSubjectId",
                table: "ResourceAnalyses",
                column: "SuggestedSubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResourceAnalyses_Topics_SuggestedTopicId",
                table: "ResourceAnalyses",
                column: "SuggestedTopicId",
                principalTable: "Topics",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Preserve existing users/resources/files and schedule only local text extraction.
            migrationBuilder.Sql("""
                INSERT INTO [ResourceSearchDocuments] ([ResourceId],[SearchText],[UpdatedAtUtc])
                SELECT r.[Id], CONCAT(r.[Title],NCHAR(10),r.[Description],NCHAR(10),
                    (SELECT STRING_AGG(CONVERT(nvarchar(max),t.[Name]),NCHAR(10)) FROM [ResourceTags] rt JOIN [Tags] t ON rt.[TagId]=t.[Id] WHERE rt.[ResourceId]=r.[Id])), SYSUTCDATETIME()
                FROM [Resources] r WHERE NOT EXISTS (SELECT 1 FROM [ResourceSearchDocuments] s WHERE s.[ResourceId]=r.[Id]);
                INSERT INTO [ResourceTextExtractions] ([ResourceId],[Status],[TotalPages],[ProcessedPages],[CharacterCount],[IsTruncated])
                SELECT r.[Id],N'Pending',0,0,0,0 FROM [Resources] r
                WHERE NOT EXISTS (SELECT 1 FROM [ResourceTextExtractions] e WHERE e.[ResourceId]=r.[Id]);
                INSERT INTO [ProcessingJobs] ([ResourceId],[FileHash],[AnalyzeRequested],[Status],[Attempts],[NotBeforeUtc],[CreatedAtUtc],[UpdatedAtUtc])
                SELECT r.[Id],r.[FileHash],0,N'Pending',0,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME()
                FROM [Resources] r WHERE NOT EXISTS (SELECT 1 FROM [ProcessingJobs] j WHERE j.[ResourceId]=r.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResourceAnalyses_Subjects_SuggestedSubjectId",
                table: "ResourceAnalyses");

            migrationBuilder.DropForeignKey(
                name: "FK_ResourceAnalyses_Topics_SuggestedTopicId",
                table: "ResourceAnalyses");

            migrationBuilder.DropTable(
                name: "PageBookmarks");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "ResourceEmbeddings");

            migrationBuilder.DropTable(
                name: "ResourceSearchDocuments");

            migrationBuilder.DropTable(
                name: "ResourceTextPages");

            migrationBuilder.DropTable(
                name: "ResourceTextExtractions");

            migrationBuilder.DropIndex(
                name: "IX_ResourceAnalyses_SuggestedSubjectId",
                table: "ResourceAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_ResourceAnalyses_SuggestedTopicId",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "AnalyzedCharacters",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "IsPartial",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "ProcessedPages",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "SourceCharacters",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "SuggestedSubjectId",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "SuggestedTopicId",
                table: "ResourceAnalyses");

            migrationBuilder.DropColumn(
                name: "TotalPages",
                table: "ResourceAnalyses");
        }
    }
}
