using FitTrackr.MAUI.Localization;

namespace FitTrackr.MAUI.Pages;

public partial class StreakDetailPage : ContentPage
{
    public StreakDetailPage(int streak, int record)
    {
        InitializeComponent();

        var loc = LocalizationResourceManager.Instance;
        StreakNumberLabel.Text = streak.ToString();
        RecordLabel.Text = record > 0 ? string.Format(loc["Main_StreakRecordFormat"], record) : loc["StreakDetail_NoRecordYet"];
        MotivationLabel.Text = GetMotivationText(streak);
    }

    private static string GetMotivationText(int streak)
    {
        var loc = LocalizationResourceManager.Instance;
        return streak switch
        {
            0 => loc["StreakDetail_Motivation0"],
            1 => loc["StreakDetail_Motivation1"],
            <= 3 => string.Format(loc["StreakDetail_MotivationUpTo3Format"], streak),
            <= 7 => string.Format(loc["StreakDetail_MotivationUpTo7Format"], streak),
            <= 12 => string.Format(loc["StreakDetail_MotivationUpTo12Format"], streak),
            <= 24 => string.Format(loc["StreakDetail_MotivationUpTo24Format"], streak),
            _ => string.Format(loc["StreakDetail_MotivationOver24Format"], streak)
        };
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(animated: true);
    }
}
