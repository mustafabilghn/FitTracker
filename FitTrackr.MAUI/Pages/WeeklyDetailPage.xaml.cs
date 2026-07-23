using FitTrackr.MAUI.Localization;

namespace FitTrackr.MAUI.Pages;

public partial class WeeklyDetailPage : ContentPage
{
    public WeeklyDetailPage(int workoutDaysThisWeek)
    {
        InitializeComponent();

        var today = DateTime.Today;
        int daysFromMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var monday = today.AddDays(-daysFromMonday);
        var sunday = monday.AddDays(6);

        CountLabel.Text = workoutDaysThisWeek.ToString();
        DateRangeLabel.Text = $"{monday:dd MMM} – {sunday:dd MMM}";
        MotivationLabel.Text = GetMotivationText(workoutDaysThisWeek);

        // Aktif günleri işaretle — WeeklyDetailPage sadece count alıyor,
        // bugüne kadar geçen günleri "geçmiş" olarak grileştir
        MarkDays(monday, today, workoutDaysThisWeek);
    }

    private void MarkDays(DateTime monday, DateTime today, int activeCount)
    {
        // Hangi günlerin geçtiğini hesapla (Pazartesi=0, ..., Pazar=6)
        int todayOffset = (int)(today - monday).TotalDays; // 0=Pzt, 6=Paz

        var dayLabels = new[] { DayMon, DayTue, DayWed, DayThu, DayFri, DaySat, DaySun };

        // Geçen günlerin kaçında antrenman var bilemeyiz (sadece toplam sayı var),
        // bu yüzden bugüne kadar olan günleri geçmiş, sonrasını gri göster
        for (int i = 0; i < 7; i++)
        {
            if (i > todayOffset)
            {
                // Henüz gelmedi
                dayLabels[i].Text = "○";
                dayLabels[i].TextColor = Color.FromArgb("#444444");
            }
            else
            {
                // Geçmiş gün — aktif mi değil mi bilmiyoruz, nötr göster
                dayLabels[i].Text = "○";
                dayLabels[i].TextColor = Color.FromArgb("#666666");
            }
        }

        // Bugünü vurgula
        if (todayOffset >= 0 && todayOffset < 7)
        {
            dayLabels[todayOffset].Text = "●";
            dayLabels[todayOffset].TextColor = Color.FromArgb("#4CAF50");
        }
    }

    private static string GetMotivationText(int count)
    {
        var loc = LocalizationResourceManager.Instance;
        return count switch
        {
            0 => loc["WeeklyDetail_Motivation0"],
            1 => loc["WeeklyDetail_Motivation1"],
            2 => loc["WeeklyDetail_Motivation2"],
            3 => loc["WeeklyDetail_Motivation3"],
            4 => loc["WeeklyDetail_Motivation4"],
            5 => loc["WeeklyDetail_Motivation5"],
            6 => loc["WeeklyDetail_Motivation6"],
            _ => loc["WeeklyDetail_Motivation7Plus"]
        };
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(animated: true);
    }
}
