using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureUpload.Migrations
{
    /// <inheritdoc />
    public partial class Files_added_field_AzureUri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureUri",
                table: "Files",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureUri",
                table: "Files");
        }
    }
}
