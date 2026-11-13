using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MapDoc.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Functoids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PresetKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsPreset = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Inputs = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsVariadic = table.Column<bool>(type: "bit", nullable: false),
                    VariadicInputName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VariadicInputType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputInference = table.Column<int>(type: "int", nullable: false),
                    OutputInferencePinIndex = table.Column<int>(type: "int", nullable: false),
                    AggregatesRepeatingInput = table.Column<bool>(type: "bit", nullable: false),
                    ConfigParams = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Functoids", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Namespace = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SourceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mappings_Types_SourceTypeId",
                        column: x => x.SourceTypeId,
                        principalTable: "Types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mappings_Types_TargetTypeId",
                        column: x => x.TargetTypeId,
                        principalTable: "Types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Properties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferencedTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsNullable = table.Column<bool>(type: "bit", nullable: false),
                    IsOptional = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Properties_Types_OwnerTypeId",
                        column: x => x.OwnerTypeId,
                        principalTable: "Types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Properties_Types_ReferencedTypeId",
                        column: x => x.ReferencedTypeId,
                        principalTable: "Types",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MappingGridPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingGridPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MappingGridPages_Mappings_MappingId",
                        column: x => x.MappingId,
                        principalTable: "Mappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingFunctoidInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GridPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FunctoidDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    LabelOverride = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ConfigValues = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingFunctoidInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MappingFunctoidInstances_Functoids_FunctoidDefinitionId",
                        column: x => x.FunctoidDefinitionId,
                        principalTable: "Functoids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MappingFunctoidInstances_MappingGridPages_GridPageId",
                        column: x => x.GridPageId,
                        principalTable: "MappingGridPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GridPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePropertyPath = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: true),
                    SourceFunctoidInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetPropertyPath = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: true),
                    TargetFunctoidInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetInputIndex = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MappingLinks_MappingFunctoidInstances_SourceFunctoidInstanceId",
                        column: x => x.SourceFunctoidInstanceId,
                        principalTable: "MappingFunctoidInstances",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MappingLinks_MappingFunctoidInstances_TargetFunctoidInstanceId",
                        column: x => x.TargetFunctoidInstanceId,
                        principalTable: "MappingFunctoidInstances",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MappingLinks_MappingGridPages_GridPageId",
                        column: x => x.GridPageId,
                        principalTable: "MappingGridPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Functoids_Category_Name",
                table: "Functoids",
                columns: new[] { "Category", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Functoids_PresetKey",
                table: "Functoids",
                column: "PresetKey");

            migrationBuilder.CreateIndex(
                name: "IX_MappingFunctoidInstances_FunctoidDefinitionId",
                table: "MappingFunctoidInstances",
                column: "FunctoidDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingFunctoidInstances_GridPageId",
                table: "MappingFunctoidInstances",
                column: "GridPageId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingGridPages_MappingId",
                table: "MappingGridPages",
                column: "MappingId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingLinks_GridPageId",
                table: "MappingLinks",
                column: "GridPageId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingLinks_SourceFunctoidInstanceId",
                table: "MappingLinks",
                column: "SourceFunctoidInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingLinks_TargetFunctoidInstanceId",
                table: "MappingLinks",
                column: "TargetFunctoidInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Mappings_SourceTypeId",
                table: "Mappings",
                column: "SourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Mappings_TargetTypeId",
                table: "Mappings",
                column: "TargetTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_OwnerTypeId",
                table: "Properties",
                column: "OwnerTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_ReferencedTypeId",
                table: "Properties",
                column: "ReferencedTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Types_Namespace_Name",
                table: "Types",
                columns: new[] { "Namespace", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MappingLinks");

            migrationBuilder.DropTable(
                name: "Properties");

            migrationBuilder.DropTable(
                name: "MappingFunctoidInstances");

            migrationBuilder.DropTable(
                name: "Functoids");

            migrationBuilder.DropTable(
                name: "MappingGridPages");

            migrationBuilder.DropTable(
                name: "Mappings");

            migrationBuilder.DropTable(
                name: "Types");
        }
    }
}
