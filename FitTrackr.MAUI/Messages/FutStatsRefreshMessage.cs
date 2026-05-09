using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FitTrackr.MAUI.Messages
{
    /// <summary>
    /// Signals that FUT card stats should be refreshed.
    /// 
    /// Used to notify MainPage when new exercise data is added,
    /// so FUT card (BP, SQ, DL, BR) max weights are updated.
    /// 
    /// Subscribers:
    /// - MainPage: Triggers FutCardStatsAsync reload
    /// </summary>
    public class FutStatsRefreshMessage : ValueChangedMessage<bool>
    {
        public FutStatsRefreshMessage() : base(true)
        {
        }
    }
}
