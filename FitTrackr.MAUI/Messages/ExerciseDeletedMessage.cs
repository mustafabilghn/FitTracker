using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FitTrackr.MAUI.Messages
{
    public class ExerciseDeletedMessage : ValueChangedMessage<Guid>
    {
        public ExerciseDeletedMessage(Guid value) : base(value)
        {
        }
    }
}
