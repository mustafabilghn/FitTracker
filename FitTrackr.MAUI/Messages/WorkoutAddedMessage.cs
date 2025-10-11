using CommunityToolkit.Mvvm.Messaging.Messages;
using FitTrackr.MAUI.Models.DTO;

namespace FitTrackr.MAUI.Messages
{
    public class WorkoutAddedMessage : ValueChangedMessage<WorkoutSummaryDto>
    {
        public WorkoutAddedMessage(WorkoutSummaryDto value) : base(value)
        {
        }
    }
}
