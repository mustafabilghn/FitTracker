using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FitTrackr.MAUI.Messages
{
    public class WorkoutSelectedMessage : ValueChangedMessage<Guid>
    {
        public WorkoutSelectedMessage(Guid value) : base(value)
        {
        }
    }
}
