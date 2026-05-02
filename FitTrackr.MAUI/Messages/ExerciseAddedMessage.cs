using CommunityToolkit.Mvvm.Messaging.Messages;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI.Messages
{
    /// <summary>
    /// Signals that a new exercise has been added to a workout.
    /// 
    /// IMPORTANT: WorkoutDate is stored as a normalized LOCAL DATE (not UTC).
    /// This ensures consistent date comparison and display across timezones.
    /// 
    /// Subscribers:
    /// - ProgressViewModel: Triggers real-time dashboard update
    /// </summary>
    public class ExerciseAddedMessage : ValueChangedMessage<Guid>
    {
        public DateTime WorkoutDate { get; }

        public ExerciseAddedMessage(Guid workoutId, DateTime workoutDate) : base(workoutId)
        {
            // RULE: Always normalize to local date to avoid timezone bugs
            // This prevents date shifting across timezone boundaries
            WorkoutDate = workoutDate.ToLocalDate();
        }
    }
}
