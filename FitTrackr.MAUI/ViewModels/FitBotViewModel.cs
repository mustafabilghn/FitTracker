using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Localization;
using FitTrackr.MAUI.Models;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI.ViewModels;

public partial class FitBotViewModel : ObservableObject
{
    private readonly WorkoutService _workoutService;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private string userInputText = string.Empty;

    public ObservableCollection<FitBotChatMessage> Messages { get; } = new();

    public ObservableCollection<QuickActionOption> QuickActions { get; } = new()
    {
        new QuickActionOption("analyze", LocalizationResourceManager.Instance["FitBot_ActionAnalyze"]),
        new QuickActionOption("today", LocalizationResourceManager.Instance["FitBot_ActionToday"]),
        new QuickActionOption("program", LocalizationResourceManager.Instance["FitBot_ActionProgram"]),
        new QuickActionOption("motivation", LocalizationResourceManager.Instance["FitBot_ActionMotivation"]),
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
            Text = LocalizationResourceManager.Instance["FitBot_WelcomeMessage"]
        });
    }

    [RelayCommand]
    private async Task QuickActionAsync(QuickActionOption? option)
    {
        if (option is null || string.IsNullOrWhiteSpace(option.Label) || IsLoading)
            return;

        var history = Messages.TakeLast(10).ToList();
        ErrorMessage = string.Empty;
        Messages.Add(new FitBotChatMessage { IsFromUser = true, Text = option.Label });
        await RunChatAsync(option.Label, option.Key, history);
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
                reply = $"{string.Format(LocalizationResourceManager.Instance["FitBot_PlateauAlertFormat"], exerciseList)}\n\n{reply}";
            }

            AddBotMessage(reply);
        }
        catch (HttpRequestException)
        {
            var friendly = LocalizationResourceManager.Instance["FitBot_ConnectionError"];
            ErrorMessage = friendly;
            AddBotMessage(friendly);
        }
        catch (Exception)
        {
            var friendly = LocalizationResourceManager.Instance["FitBot_UnexpectedError"];
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

public sealed record QuickActionOption(string Key, string Label);
