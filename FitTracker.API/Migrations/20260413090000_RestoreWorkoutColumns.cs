using System;
using FitTrackr.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitTrackr.API.Migrations
{
    [DbContext(typeof(FitTrackrDbContext))]
    [Migration("20260413090000_RestoreWorkoutColumns")]
    public partial class RestoreWorkoutColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Workouts', 'DurationMinutes') IS NULL
BEGIN
    ALTER TABLE dbo.Workouts ADD DurationMinutes float NOT NULL CONSTRAINT DF_Workouts_DurationMinutes DEFAULT(0);
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Workouts', 'LocationId') IS NULL
BEGIN
    ALTER TABLE dbo.Workouts ADD LocationId uniqueidentifier NOT NULL CONSTRAINT DF_Workouts_LocationId DEFAULT('2B0090B3-8BCD-49E7-A101-D0A14F13CE56');
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Workouts_LocationId' AND object_id = OBJECT_ID('dbo.Workouts'))
BEGIN
    CREATE INDEX IX_Workouts_LocationId ON dbo.Workouts(LocationId);
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Workouts_Locations_LocationId')
BEGIN
    ALTER TABLE dbo.Workouts WITH CHECK ADD CONSTRAINT FK_Workouts_Locations_LocationId FOREIGN KEY(LocationId)
    REFERENCES dbo.Locations (Id) ON DELETE CASCADE;
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Workouts_Locations_LocationId')
BEGIN
    ALTER TABLE dbo.Workouts DROP CONSTRAINT FK_Workouts_Locations_LocationId;
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Workouts_LocationId' AND object_id = OBJECT_ID('dbo.Workouts'))
BEGIN
    DROP INDEX IX_Workouts_LocationId ON dbo.Workouts;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Workouts', 'LocationId') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Workouts DROP CONSTRAINT DF_Workouts_LocationId;
    ALTER TABLE dbo.Workouts DROP COLUMN LocationId;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Workouts', 'DurationMinutes') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Workouts DROP CONSTRAINT DF_Workouts_DurationMinutes;
    ALTER TABLE dbo.Workouts DROP COLUMN DurationMinutes;
END
");
        }
    }
}
