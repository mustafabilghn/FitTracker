using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FitTrackr.MAUI.Messages
{
    public class ExerciseAddedMessage : ValueChangedMessage<Guid>
    {
        public ExerciseAddedMessage(Guid value) : base(value)
        {
        }
    }
}
