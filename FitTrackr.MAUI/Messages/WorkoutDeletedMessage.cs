using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FitTrackr.MAUI.Messages
{
    public class WorkoutDeletedMessage : ValueChangedMessage<Guid>
    {
        public WorkoutDeletedMessage(Guid value) : base(value)
        {
        }
    }
}
