using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Models;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI.ViewModels;

public partial class FitBotViewModel : ObservableObject
{
    public const string ActionAnalyze = "Antrenmanlarımı analiz et";
    public const string ActionToday = "Bugün ne çalışayım?";
    public const string ActionProgram = "Programımı değerlendir";
    public const string ActionMotivation = "Motivasyon ver";

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
                + "Hazırsan aşağıdan bir seçenek seçelim veya direkt soru sorabilirsin."
        });
    }

    [RelayCommand]
    private async Task QuickActionAsync(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || IsLoading)
            return;

        var actionType = label switch
        {
            ActionAnalyze => "analyze",
            ActionToday => "today",
            ActionProgram => "program",
            ActionMotivation => "motivation",
            _ => "free"
        };

        var history = Messages.TakeLast(10).ToList();
        ErrorMessage = string.Empty;
        Messages.Add(new FitBotChatMessage { IsFromUser = true, Text = label });
        await RunChatAsync(label, actionType, history);
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (IsLoading)
            return;

        var text = UserInputText?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        var history = Messages.TakeLast(10).ToList();
        ErrorMessage = string.Empty;
        Messages.Add(new FitBotChatMessage { IsFromUser = true, Text = text });
        UserInputText = string.Empty;
        await RunChatAsync(text, "free", history);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    private async Task RunChatAsync(string message, string actionType, IEnumerable<FitBotChatMessage> history)
    {
        IsLoading = true;
        try
        {
            var response = await _workoutService.SendFitBotMessageAsync(message, actionType, history);

            var reply = response.Reply;
            if (response.PlateauAlerts?.Count > 0)
            {
                var exerciseList = string.Join(", ", response.PlateauAlerts);
                reply = $"⚠️ Plato uyarısı: {exerciseList}\n\n{reply}";
            }

            AddBotMessage(reply);
        }
        catch (HttpRequestException)
        {
            const string friendly = "Yanıt alınamadı. Bağlantını kontrol edip tekrar dene.";
            ErrorMessage = friendly;
            AddBotMessage(friendly);
        }
        catch (Exception)
        {
            const string friendly = "Beklenmeyen bir sorun oluştu. Lütfen daha sonra tekrar dene.";
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
}
