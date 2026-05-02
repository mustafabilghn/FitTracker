using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models;
using FitTrackr.MAUI.Models.DTO;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class DailyWorkoutCardViewModel : ObservableObject
    {
        public const string DefaultWorkoutName = "Antrenman";

        private Guid? workoutId;
        private string workoutName = DefaultWorkoutName;
        private bool isExpanded;
        private bool hasExercises;
        private string exercisesPreviewText = "Egzersiz bulunmuyor.";

        public Guid? WorkoutId
        {
            get => workoutId;
            private set => SetProperty(ref workoutId, value);
        }

        public DateTime WorkoutDate { get; }

        public string WorkoutDateText => WorkoutDateDisplay.FormatDateAndWeekday(WorkoutDate);

        public string WorkoutName
        {
            get => workoutName;
            private set => SetProperty(ref workoutName, NormalizeWorkoutName(value));
        }

        public bool IsExpanded
        {
            get => isExpanded;
            set => SetProperty(ref isExpanded, value);
        }

        public bool HasExercises
        {
            get => hasExercises;
            private set => SetProperty(ref hasExercises, value);
        }

        public string ExercisesPreviewText
        {
            get => exercisesPreviewText;
            private set => SetProperty(ref exercisesPreviewText, value);
        }

        public ObservableCollection<WorkoutExerciseItemViewModel> Exercises { get; } = new();

        public bool HasPersistedWorkout => WorkoutId.HasValue;

        public DailyWorkoutCardViewModel(DateTime workoutDate, WorkoutSummaryDto? workout = null)
        {
            WorkoutDate = workoutDate.Date;
            ApplyWorkout(workout);
        }

        public void ApplyWorkout(WorkoutSummaryDto? workout)
        {
            if (workout is null)
            {
                WorkoutId = null;
                WorkoutName = DefaultWorkoutName;
                SetExercises([]);
                return;
            }

            WorkoutId = workout.Id;
            WorkoutName = workout.WorkoutName;
        }

        public void UpdateExerciseState(bool hasExercises)
        {
            HasExercises = hasExercises;

            if (!HasExercises)
            {
                IsExpanded = false;
                ExercisesPreviewText = "Egzersiz bulunmuyor.";
            }
        }

        public void SetExerciseNames(IEnumerable<string?> exerciseNames)
        {
            var names = exerciseNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .ToList();

            if (names.Count == 0)
            {
                ExercisesPreviewText = "Egzersiz bulunmuyor.";
                return;
            }

            ExercisesPreviewText = string.Join("\n", names.Select((name, index) => $"{index + 1}. {name}"));
        }

        public void SetExercises(IEnumerable<ExerciseDto> exercises)
        {
            Exercises.Clear();

            foreach (var exercise in exercises.Where(e => e != null))
            {
                var setItems = (exercise.ExerciseSets ?? [])
                    .OrderBy(s => s.SetNumber)
                    .Select(s => new WorkoutExerciseSetItemViewModel(s.SetNumber, s.Reps, s.WeightInKg));

                Exercises.Add(new WorkoutExerciseItemViewModel(exercise.Id, exercise.ExerciseName, setItems));
            }

            HasExercises = Exercises.Count > 0;
            ExercisesPreviewText = HasExercises
                ? $"{Exercises.Count} egzersiz"
                : "Egzersiz bulunmuyor.";

            if (!HasExercises)
            {
                IsExpanded = false;
            }
        }

        public void RemoveExercise(Guid exerciseId)
        {
            var exercise = Exercises.FirstOrDefault(e => e.ExerciseId == exerciseId);
            if (exercise == null)
            {
                return;
            }

            Exercises.Remove(exercise);
            HasExercises = Exercises.Count > 0;
            ExercisesPreviewText = HasExercises
                ? $"{Exercises.Count} egzersiz"
                : "Egzersiz bulunmuyor.";

            if (!HasExercises)
            {
                IsExpanded = false;
            }
        }

        public void UpdateWorkoutName(string? workoutName)
        {
            WorkoutName = workoutName;
        }

        public void UpdateWorkoutId(Guid workoutId)
        {
            WorkoutId = workoutId;
        }

        private static string NormalizeWorkoutName(string? workoutName)
        {
            return string.IsNullOrWhiteSpace(workoutName)
                ? DefaultWorkoutName
                : workoutName.Trim();
        }
    }

    public class WorkoutExerciseItemViewModel
    {
        public Guid ExerciseId { get; }

        public string ExerciseName { get; }

        public ObservableCollection<WorkoutExerciseSetItemViewModel> Sets { get; } = new();

        public WorkoutExerciseItemViewModel(Guid exerciseId, string exerciseName, IEnumerable<WorkoutExerciseSetItemViewModel> sets)
        {
            ExerciseId = exerciseId;
            ExerciseName = string.IsNullOrWhiteSpace(exerciseName) ? "Egzersiz" : exerciseName.Trim();

            foreach (var set in sets)
            {
                Sets.Add(set);
            }
        }
    }

    public class WorkoutExerciseSetItemViewModel
    {
        public int SetNumber { get; }

        public string Reps { get; }

        public double WeightInKg { get; }

        public string SetLine => $"Set {SetNumber} • {Reps} x {WeightInKg:0.##} kg";

        public WorkoutExerciseSetItemViewModel(int setNumber, string reps, double weightInKg)
        {
            SetNumber = setNumber;
            Reps = string.IsNullOrWhiteSpace(reps) ? "0" : reps.Trim();
            WeightInKg = weightInKg;
        }
    }
}
