namespace FitTrackr.MAUI.Pages;

public partial class StreakDetailPage : ContentPage
{
    public StreakDetailPage(int streak, int record)
    {
        InitializeComponent();

        StreakNumberLabel.Text = streak.ToString();
        RecordLabel.Text = record > 0 ? $"Rekor: {record} Hafta" : "Henüz rekor yok";
        MotivationLabel.Text = GetMotivationText(streak);
    }

    private static string GetMotivationText(int streak) => streak switch
    {
        0 => "Henüz serini başlatmadın. Bu hafta bir antrenman kaydet ve ilk haftanı tamamla!",
        1 => "İlk haftanı tamamladın! Devam et, serini büyüt.",
        <= 3 => $"{streak} haftalık bir seri yakaladın. Ritmini bulmaya başlıyorsun!",
        <= 7 => $"{streak} hafta üst üste antrenman yaptın. Harika bir alışkanlık kuruyorsun!",
        <= 12 => $"{streak} haftalık seri! Artık bu bir yaşam biçimi.",
        <= 24 => $"{streak} hafta! Yarım yıla yaklaştın — bu ciddi bir kararlılık.",
        _ => $"{streak} haftalık inanılmaz bir seri! Sen bir fitness makinesisin."
    };

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(animated: true);
    }
}
