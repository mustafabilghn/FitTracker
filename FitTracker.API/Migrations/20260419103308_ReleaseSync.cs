using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitTrackr.API.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Workouts_Locations_LocationId",
                table: "Workouts");

            migrationBuilder.DropIndex(
                name: "IX_Workouts_LocationId",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Workouts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DurationMinutes",
                table: "Workouts",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Workouts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_LocationId",
                table: "Workouts",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Workouts_Locations_LocationId",
                table: "Workouts",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
