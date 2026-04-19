using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Data
{
    public static class ExerciseCatalogSeed
    {
        public static IReadOnlyList<ExerciseCatalogItemDto> Items { get; } =
        [
            Create("Barbell Bench Press", "Chest", "Barbell", "Intermediate"),
            Create("Incline Dumbbell Press", "Chest", "Dumbbell", "Beginner"),
            Create("Push-Up", "Chest", "Bodyweight", "Beginner"),
            Create("Cable Chest Fly", "Chest", "Cable", "Beginner"),
            Create("Machine Chest Press", "Chest", "Machine", "Beginner"),

            Create("Pull-Up", "Back", "Bodyweight", "Intermediate"),
            Create("Lat Pulldown", "Back", "Cable", "Beginner"),
            Create("Barbell Row", "Back", "Barbell", "Intermediate"),
            Create("Seated Cable Row", "Back", "Cable", "Beginner"),
            Create("Single Arm Dumbbell Row", "Back", "Dumbbell", "Beginner"),

            Create("Back Squat", "Legs", "Barbell", "Intermediate"),
            Create("Romanian Deadlift", "Legs", "Barbell", "Intermediate"),
            Create("Leg Press", "Legs", "Machine", "Beginner"),
            Create("Walking Lunge", "Legs", "Dumbbell", "Beginner"),
            Create("Leg Extension", "Legs", "Machine", "Beginner"),
            Create("Hamstring Curl", "Legs", "Machine", "Beginner"),
            Create("Standing Calf Raise", "Legs", "Machine", "Beginner"),

            Create("Overhead Press", "Shoulders", "Barbell", "Intermediate"),
            Create("Dumbbell Shoulder Press", "Shoulders", "Dumbbell", "Beginner"),
            Create("Lateral Raise", "Shoulders", "Dumbbell", "Beginner"),
            Create("Rear Delt Fly", "Shoulders", "Dumbbell", "Beginner"),
            Create("Face Pull", "Shoulders", "Cable", "Beginner"),

            Create("Barbell Curl", "Arms", "Barbell", "Beginner"),
            Create("Hammer Curl", "Arms", "Dumbbell", "Beginner"),
            Create("Triceps Pushdown", "Arms", "Cable", "Beginner"),
            Create("Skull Crusher", "Arms", "Barbell", "Intermediate"),
            Create("Dips", "Arms", "Bodyweight", "Intermediate"),

            Create("Plank", "Core", "Bodyweight", "Beginner"),
            Create("Hanging Leg Raise", "Core", "Bodyweight", "Intermediate"),
            Create("Cable Crunch", "Core", "Cable", "Beginner"),
            Create("Russian Twist", "Core", "Bodyweight", "Beginner"),
            Create("Ab Wheel Rollout", "Core", "Bodyweight", "Intermediate"),

            Create("Deadlift", "Full Body", "Barbell", "Advanced"),
            Create("Front Squat", "Legs", "Barbell", "Intermediate"),
            Create("Hip Thrust", "Legs", "Barbell", "Beginner"),
            Create("Goblet Squat", "Legs", "Dumbbell", "Beginner"),
            Create("Bulgarian Split Squat", "Legs", "Dumbbell", "Intermediate"),
            Create("Chest Supported Row", "Back", "Machine", "Beginner"),
            Create("Arnold Press", "Shoulders", "Dumbbell", "Intermediate"),
            Create("Cable Lateral Raise", "Shoulders", "Cable", "Beginner")
        ];

        private static ExerciseCatalogItemDto Create(string name, string bodyPart, string equipment, string level)
        {
            return new ExerciseCatalogItemDto
            {
                Id = Guid.NewGuid(),
                Name = name,
                BodyPart = bodyPart,
                Equipment = equipment,
                Level = level,
            };
        }
    }
}