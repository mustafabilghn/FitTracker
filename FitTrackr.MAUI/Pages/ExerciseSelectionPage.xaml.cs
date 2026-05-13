using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class ExerciseSelectionPage : ContentPage
{
    private readonly ExerciseSelectionViewModel _viewModel;
    private Guid _workoutId;   // mutable: ilk egzersiz yeni workout oluşturduğunda güncellenir
    private readonly DateTime _workoutDate;
    private readonly string _workoutName;
    private bool _isLoaded;
    private bool _messageRegistered;

    public ExerciseSelectionPage(
        ExerciseSelectionViewModel viewModel,
        Guid workoutId,
        DateTime workoutDate,
        string workoutName)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _workoutId = workoutId;
        _workoutDate = workoutDate.Date;
        _workoutName = string.IsNullOrWhiteSpace(workoutName) ? "Antrenman" : workoutName;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // ExerciseAddedMessage'ı bir kez dinle:
        // İlk egzersiz Guid.Empty'den yeni bir workout oluşturursa ID'yi öğrenip
        // sonraki egzersizleri aynı workuta ekleyebiliriz.
        if (!_messageRegistered)
        {
            WeakReferenceMessenger.Default.Register<ExerciseAddedMessage>(this, OnExerciseAdded);
            _messageRegistered = true;
        }

        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await _viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Sadece bu sayfa navigation stack'ten tamamen çıktığında unregister et.
        // ExerciseSetEntryPage push edildiğinde de OnDisappearing çalışır,
        // ama o zaman hâlâ stack'teyiz — mesajı kaçırmamak için unregister ETME.
        if (_messageRegistered && !Navigation.NavigationStack.Contains(this))
        {
            WeakReferenceMessenger.Default.Unregister<ExerciseAddedMessage>(this);
            _messageRegistered = false;
        }
    }

    /// <summary>
    /// İlk egzersiz kaydedildiğinde ExerciseSetEntryViewModel bu mesajı gönderir.
    /// Eğer _workoutId boşsa (yeni gün, henüz workout yok), oluşturulan workout ID'sini sakla.
    /// Böylece ikinci, üçüncü... egzersizler aynı workout'a eklenir.
    /// </summary>
    private void OnExerciseAdded(object recipient, ExerciseAddedMessage message)
    {
        if (_workoutId == Guid.Empty)
        {
            _workoutId = message.Value;
        }
    }

    private async void OnExerciseTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not ExerciseCatalogItemDto selectedExercise)
        {
            return;
        }

        var services = Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("Page services are not available.");

        var setEntryPage = ActivatorUtilities.CreateInstance<ExerciseSetEntryPage>(
            services,
            selectedExercise,
            _workoutId,        // artık güncel: ilk egzersizden sonra doğru workout ID
            _workoutDate,
            _workoutName);

        await Navigation.PushAsync(setEntryPage);
    }
}
