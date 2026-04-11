using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Models;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI.ViewModels;

public partial class FitBotViewModel : ObservableObject
{
    public const string ActionAnalyze = "Antrenmanlarımı analiz et";
    public const string ActionToday = "Bugün ne çalışayım?";
    public const string ActionProgram = "Programımı değerlendir";
    public const string ActionMotivation = "Motivasyon ver";

    private const string MsgToday =
        "Bugün güvenli ve dengeli bir tam vücut gününe gidebilirsin. 5-10 dakika ısın, ardından bir bacak hareketi, bir itiş hareketi, bir çekiş hareketi ve kısa bir core bölümü ekle.\n\n"
        + "Enerjin düşükse süreyi kısaltıp formu temiz tutman yeterli olur.";

    private const string MsgProgram =
        "Şu an programını en sağlıklı şekilde antrenman kayıtlarına bakarak yorumlayabiliyorum. Daha detaylı program değerlendirmesi yakında eklenecek.\n\n"
        + "İstersen önce 'Antrenmanlarımı analiz et' ile mevcut verine göre hızlı bir değerlendirme alabilirsin.";

    private const string MsgMotivation =
        "Mükemmel olmak zorunda değilsin. Bugün kısa da olsa antrenmana başlamak, hiç başlamamaktan daha iyi.\n\n"
        + "Ritmi koru, gerisi zamanla gelir.";

    private const string MsgFreeChatPlaceholder =
        "Serbest sohbeti henüz açmadım, ama hızlı seçeneklerle yardımcı olabilirim. İstersen aşağıdan bir konu seç ve oradan devam edelim.";

    private const string MsgUnsupportedAction =
        "Bu seçenek şu an hazır değil. Aşağıdaki hızlı seçeneklerden biriyle devam edebilirsin.";

    private readonly WorkoutService _workoutService;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private string userInputText = string.Empty;

    public ObservableCollection<FitBotChatMessage> Messages { get; } = new();

    public ObservableCollection<string> QuickActions { get; } = new()
    {
        ActionAnalyze,
        ActionToday,
        ActionProgram,
        ActionMotivation
    };

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public FitBotViewModel(WorkoutService workoutService)
    {
        _workoutService = workoutService;
        AppendWelcomeMessage();
    }

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    private void AppendWelcomeMessage()
    {
        Messages.Add(new FitBotChatMessage
        {
            IsFromUser = false,
            Text =
                "Merhaba, ben FitBot.\n\n"
                + "Antrenman kayıtlarına bakıp kısa yorumlar ve pratik öneriler sunabilirim.\n\n"
                + "Hazırsan aşağıdan bir seçenek seçelim."
        });
    }

    [RelayCommand]
    private async Task QuickActionAsync(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        if (IsLoading)
            return;

        ErrorMessage = string.Empty;
        Messages.Add(new FitBotChatMessage { IsFromUser = true, Text = label });

        if (label == ActionAnalyze)
        {
            await RunAnalyzeAsync();
            return;
        }

        if (label == ActionToday)
        {
            AddBotMessage(MsgToday);
            return;
        }

        if (label == ActionProgram)
        {
            AddBotMessage(MsgProgram);
            return;
        }

        if (label == ActionMotivation)
        {
            AddBotMessage(MsgMotivation);
            return;
        }

        AddBotMessage(MsgUnsupportedAction);
    }

    [RelayCommand]
    private void SendMessage()
    {
        if (IsLoading)
            return;

        var text = UserInputText?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        ErrorMessage = string.Empty;
        Messages.Add(new FitBotChatMessage { IsFromUser = true, Text = text });
        UserInputText = string.Empty;
        AddBotMessage(MsgFreeChatPlaceholder);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    private async Task RunAnalyzeAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var insight = await _workoutService.GetAiInsightsAsync();
            AddBotMessage(FormatInsights(insight));
        }
        catch (HttpRequestException)
        {
            const string friendly =
                "Antrenman analizi alınamadı. Bağlantını kontrol edip biraz sonra tekrar dene.";
            ErrorMessage = friendly;
            AddBotMessage(friendly);
        }
        catch (Exception)
        {
            const string friendly =
                "Beklenmeyen bir sorun oluştu. Lütfen daha sonra tekrar dene.";
            ErrorMessage = friendly;
            AddBotMessage(friendly);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void AddBotMessage(string text)
    {
        Messages.Add(new FitBotChatMessage { IsFromUser = false, Text = text });
    }

    private static string FormatInsights(AiWorkoutInsightDto dto)
    {
        var summary = dto.Summary?.Trim() ?? string.Empty;
        var strengths = (dto.Strengths ?? new List<string>())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToList();
        var improvements = (dto.Improvements ?? new List<string>())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToList();
        var nextWorkoutSuggestion = dto.NextWorkoutSuggestion?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(summary) &&
            strengths.Count == 0 &&
            improvements.Count == 0 &&
            string.IsNullOrWhiteSpace(nextWorkoutSuggestion))
        {
            return "Henüz anlamlı bir yorum üretecek kadar veri görünmüyor.\n\n"
                 + "Birkaç antrenman daha ekledikten sonra tekrar deneyebilirsin.";
        }

        var sb = new StringBuilder();

        AppendParagraphSection(sb, "Özet", summary);
        AppendBulletSection(sb, "Güçlü yönler", strengths);
        AppendBulletSection(sb, "Gelişim alanları", improvements);
        AppendParagraphSection(sb, "Sonraki antrenman önerisi", nextWorkoutSuggestion);

        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result)
            ? "Analiz verisi boş döndü. Biraz sonra tekrar deneyebilirsin."
            : result;
    }

    private static void AppendParagraphSection(StringBuilder sb, string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        AppendSectionSpacing(sb);
        sb.AppendLine(title);
        sb.AppendLine(content);
    }

    private static void AppendBulletSection(StringBuilder sb, string title, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0)
            return;

        AppendSectionSpacing(sb);
        sb.AppendLine(title);

        foreach (var item in items)
        {
            sb.Append("• ");
            sb.AppendLine(item);
        }
    }

    private static void AppendSectionSpacing(StringBuilder sb)
    {
        if (sb.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
        }
    }
}
