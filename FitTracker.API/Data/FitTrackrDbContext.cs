using FitTrackr.API.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Data
{
    public class FitTrackrDbContext : DbContext
    {
        public FitTrackrDbContext(DbContextOptions<FitTrackrDbContext> dbContextOptions) : base(dbContextOptions)
        {
        }

        public DbSet<Workout> Workouts { get; set; }
        public DbSet<Intensity> Intensities { get; set; }
        public DbSet<Location> Locations { get; set; }
        public DbSet<Exercise> Exercises { get; set; }
        public DbSet<ExerciseSet> ExerciseSets { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            List<Intensity> intensities = new List<Intensity>
            {
                new Intensity
                {
                    Id = Guid.Parse("04faaf32-4a41-4b4e-888f-9651092caa08"),
                    Level = "Low"
                },
                new Intensity
                {
                    Id = Guid.Parse("153480fc-718b-4610-bd4f-ead66fb24a3d"),
                    Level = "Medium"
                },
                new Intensity
                {
                    Id = Guid.Parse("7d4ae440-c208-4b50-b252-88730b550d25"),
                    Level = "High"
                }
            };

            modelBuilder.Entity<Intensity>().HasData(intensities);

            List<Location> locations = new List<Location>
            {
                new Location
                {
                    Id = Guid.Parse("b2292de7-dc29-4675-94aa-304b3ce91bf6"),
                    LocationName = "Park"
                },
                new Location
                {
                    Id = Guid.Parse("2b0090b3-8bcd-49e7-a101-d0a14f13ce56"),
                    LocationName = "Home",
                },
                new Location
                {
                    Id = Guid.Parse("62bc269f-ebb0-477e-896b-f4212bf07111"),
                    LocationName = "Gym"
                }
            };

            modelBuilder.Entity<Location>().HasData(locations);
        }

    }

}
