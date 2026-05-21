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

    private static string GetMotivationText(int count) => count switch
    {
        0 => "Bu hafta henüz hiç antrenman kaydı yok. Hadi başla, haftayı kurtarabiliriz! 💪",
        1 => "Bu hafta 1 gün antrenman yaptın. Güzel bir başlangıç, devam et!",
        2 => "Bu hafta 2 gün aktifsin. Haftaya 3+ güne ulaşmayı hedefle.",
        3 => "Bu hafta 3 gün antrenman! Harika bir denge kuruyorsun.",
        4 => "Bu hafta 4 gün! Vücudun sana teşekkür ediyor.",
        5 => "Bu hafta 5 gün! Ciddi bir kararlılık bu.",
        6 => "Bu hafta 6 gün! Neredeyse mükemmel bir hafta.",
        _ => "Bu hafta her gün antrenman yaptın! Olağanüstü bir hafta! 🏆"
    };

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(animated: true);
    }
}
