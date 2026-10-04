using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitTrackr.API.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 = WorkoutStatus.Completed: mevcut tum kayitlar "yapilmis antrenman" olarak kalir (veri kaybi yok).
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Workouts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Workouts");
        }
    }
}
