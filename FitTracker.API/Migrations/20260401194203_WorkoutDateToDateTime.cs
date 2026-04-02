using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitTrackr.API.Migrations
{
    /// <inheritdoc />
    public partial class WorkoutDateToDateTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy column stored DayOfWeek as int (EF enum: 0=Sunday .. 6=Saturday).
            // A single weekday does not map to a real calendar date without guessing.
            // Each row is assigned a UTC calendar date in the week of migration whose weekday matches the stored value.
            // Original calendar dates are not recoverable; only the day-of-week choice is preserved for display.
            migrationBuilder.AddColumn<DateTime>(
                name: "WorkoutDateNew",
                table: "Workouts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE dbo.Workouts
                SET WorkoutDateNew = DATEADD(DAY,
                    -((DATEPART(WEEKDAY, CAST(GETUTCDATE() AS DATE)) - 1 - WorkoutDate + 7) % 7),
                    CAST(GETUTCDATE() AS DATE));");

            migrationBuilder.DropColumn(
                name: "WorkoutDate",
                table: "Workouts");

            migrationBuilder.RenameColumn(
                name: "WorkoutDateNew",
                table: "Workouts",
                newName: "WorkoutDate");

            migrationBuilder.AlterColumn<DateTime>(
                name: "WorkoutDate",
                table: "Workouts",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkoutDateOld",
                table: "Workouts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Best-effort rollback: map calendar date back to .NET DayOfWeek (Sunday=0).
            migrationBuilder.Sql(@"
                UPDATE dbo.Workouts
                SET WorkoutDateOld = (DATEPART(WEEKDAY, WorkoutDate) - 1 + 7) % 7;");

            migrationBuilder.DropColumn(
                name: "WorkoutDate",
                table: "Workouts");

            migrationBuilder.RenameColumn(
                name: "WorkoutDateOld",
                table: "Workouts",
                newName: "WorkoutDate");
        }
    }
}
