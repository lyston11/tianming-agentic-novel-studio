using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddKernelModelPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "input_price_per_million",
                table: "model_kernel_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "output_price_per_million",
                table: "model_kernel_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "input_price_per_million",
                table: "model_kernel_configurations");

            migrationBuilder.DropColumn(
                name: "output_price_per_million",
                table: "model_kernel_configurations");
        }
    }
}
