using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsuranceManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConcurrencyAndActiveCarInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Policies",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "UX_Policy_OneActiveCarPerCustomer",
                table: "Policies",
                columns: new[] { "CustomerId", "Type", "Status" },
                unique: true,
                filter: "[Type] = 'Car' AND [Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Policy_OneActiveCarPerCustomer",
                table: "Policies");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Policies");
        }
    }
}
