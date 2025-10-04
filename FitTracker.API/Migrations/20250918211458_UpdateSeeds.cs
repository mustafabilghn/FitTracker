using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitTrackr.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Intensities",
                keyColumn: "Id",
                keyValue: new Guid("04faaf32-4a41-4b4e-888f-9651092caa08"),
                column: "Level",
                value: "Düşük");

            migrationBuilder.UpdateData(
                table: "Intensities",
                keyColumn: "Id",
                keyValue: new Guid("153480fc-718b-4610-bd4f-ead66fb24a3d"),
                column: "Level",
                value: "Orta");

            migrationBuilder.UpdateData(
                table: "Intensities",
                keyColumn: "Id",
                keyValue: new Guid("7d4ae440-c208-4b50-b252-88730b550d25"),
                column: "Level",
                value: "Yüksek");

            migrationBuilder.UpdateData(
                table: "Locations",
                keyColumn: "Id",
                keyValue: new Guid("2b0090b3-8bcd-49e7-a101-d0a14f13ce56"),
                column: "LocationName",
                value: "Ev");

            migrationBuilder.UpdateData(
                table: "Locations",
                keyColumn: "Id",
                keyValue: new Guid("62bc269f-ebb0-477e-896b-f4212bf07111"),
                column: "LocationName",
                value: "Spor Salonu");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Intensities",
                keyColumn: "Id",
                keyValue: new Guid("04faaf32-4a41-4b4e-888f-9651092caa08"),
                column: "Level",
                value: "Low");

            migrationBuilder.UpdateData(
                table: "Intensities",
                keyColumn: "Id",
                keyValue: new Guid("153480fc-718b-4610-bd4f-ead66fb24a3d"),
                column: "Level",
                value: "Medium");

            migrationBuilder.UpdateData(
                table: "Intensities",
                keyColumn: "Id",
                keyValue: new Guid("7d4ae440-c208-4b50-b252-88730b550d25"),
                column: "Level",
                value: "High");

            migrationBuilder.UpdateData(
                table: "Locations",
                keyColumn: "Id",
                keyValue: new Guid("2b0090b3-8bcd-49e7-a101-d0a14f13ce56"),
                column: "LocationName",
                value: "Home");

            migrationBuilder.UpdateData(
                table: "Locations",
                keyColumn: "Id",
                keyValue: new Guid("62bc269f-ebb0-477e-896b-f4212bf07111"),
                column: "LocationName",
                value: "Gym");
        }
    }
}
