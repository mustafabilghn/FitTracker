using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;

namespace FitTrackr.MAUI.Pages;

public partial class WorkoutListPage : ContentPage
{
    private readonly WorkoutListViewModel _viewModel;
    private readonly IServiceProvider serviceProvider;
    private DateTime _calendarMonthDate;
    private bool _isCalendarPanelVisible;

    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = new();

    public bool IsCalendarPanelVisible
    {
        get => _isCalendarPanelVisible;
        private set
        {
            if (_isCalendarPanelVisible == value)
            {
                return;
            }

            _isCalendarPanelVisible = value;
            OnPropertyChanged();
        }
    }

    public string CalendarMonthTitle => _calendarMonthDate.ToString("MMMM yyyy", new CultureInfo("tr-TR"));

    public WorkoutListPage(WorkoutListViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        this.serviceProvider = serviceProvider;

        _calendarMonthDate = new DateTime(_viewModel.SelectedDate.Year, _viewModel.SelectedDate.Month, 1);
        BuildCalendarDays();

        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;

        WeakReferenceMessenger.Default.Register<WorkoutAddedMessage>(this, async (r, m) =>
        {
            await RefreshWorkoutDataAsync();
        });

        WeakReferenceMessenger.Default.Register<WorkoutDeletedMessage>(this, async (r, m) =>
        {
            await RefreshWorkoutDataAsync();
        });

        WeakReferenceMessenger.Default.Register<ExerciseDeletedMessage>(this, async (r, m) =>
        {
            await RefreshWorkoutDataAsync();
        });

        WeakReferenceMessenger.Default.Register<WorkoutSelectedMessage>(this, async (r, m) =>
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                var workoutDetailPage = serviceProvider.GetService<WorkoutDetailPage>();
                if (workoutDetailPage == null)
                    return;

                if (workoutDetailPage.BindingContext is WorkoutDetailViewModel vm)
                    await vm.LoadWorkoutDetailsAsync(m.Value);

                await Navigation.PushAsync(workoutDetailPage);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {

            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", $"Sayfa yuklenirken bir hata meydana geldi: {ex.Message}", "Tamam");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await RefreshWorkoutDataAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {

        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", $"Antrenmanlar yuklenirken bir hata meydana geldi: {ex.Message}", "Tamam");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            WorkoutsCollection.IsVisible = true;
        }
    }

    private async Task RefreshWorkoutDataAsync()
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        WorkoutsCollection.IsVisible = false;

        await _viewModel.LoadWorkoutsAsync();
        SyncCalendarToSelectedDate();

        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;
        WorkoutsCollection.IsVisible = true;
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkoutListViewModel.SelectedDate))
        {
            SyncCalendarToSelectedDate();
        }
    }

    private async void OnAddExerciseClicked(object sender, EventArgs e)
    {
        var card = sender switch
        {
            Button { CommandParameter: DailyWorkoutCardViewModel vm } => vm,
            ImageButton { CommandParameter: DailyWorkoutCardViewModel vm } => vm,
            BindableObject { BindingContext: DailyWorkoutCardViewModel vm } => vm,
            _ => null
        };

        if (card is null)
        {
            return;
        }

        await NavigateToExerciseSelectionAsync(card);
    }
    private async void OnWorkoutCardTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not DailyWorkoutCardViewModel card)
        {
            return;
        }

        await NavigateToExerciseSelectionAsync(card);
    }

    private async void OnExistingExerciseTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not WorkoutExerciseItemViewModel exercise)
        {
            return;
        }

        var services = Handler?.MauiContext?.Services ?? IPlatformApplication.Current.Services;

        var setEntryPage = ActivatorUtilities.CreateInstance<ExerciseSetEntryPage>(
            services,
            exercise.ExerciseId,
            exercise.ExerciseName);

        await Navigation.PushAsync(setEntryPage);
    }

    private void OnCalendarClicked(object sender, EventArgs e)
    {
        if (!IsCalendarPanelVisible)
        {
            SyncCalendarToSelectedDate();
        }

        IsCalendarPanelVisible = !IsCalendarPanelVisible;
    }

    private void OnPrevCalendarMonthClicked(object sender, EventArgs e)
    {
        _calendarMonthDate = _calendarMonthDate.AddMonths(-1);
        OnPropertyChanged(nameof(CalendarMonthTitle));
        BuildCalendarDays();
    }

    private void OnNextCalendarMonthClicked(object sender, EventArgs e)
    {
        _calendarMonthDate = _calendarMonthDate.AddMonths(1);
        OnPropertyChanged(nameof(CalendarMonthTitle));
        BuildCalendarDays();
    }

    private void OnCalendarDayTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not CalendarDayItem day)
        {
            return;
        }

        _viewModel.SelectedDate = day.Date;
        SyncCalendarToSelectedDate();
        IsCalendarPanelVisible = false;
    }

    private void SyncCalendarToSelectedDate()
    {
        _calendarMonthDate = new DateTime(_viewModel.SelectedDate.Year, _viewModel.SelectedDate.Month, 1);
        OnPropertyChanged(nameof(CalendarMonthTitle));
        BuildCalendarDays();
    }

    private void BuildCalendarDays()
    {
        CalendarDays.Clear();

        var firstDayOfMonth = new DateTime(_calendarMonthDate.Year, _calendarMonthDate.Month, 1);
        var startOffset = (7 + (firstDayOfMonth.DayOfWeek - DayOfWeek.Monday)) % 7;
        var gridStart = firstDayOfMonth.AddDays(-startOffset);

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            CalendarDays.Add(new CalendarDayItem
            {
                Date = date,
                DayNumber = date.Day.ToString(CultureInfo.InvariantCulture),
                IsCurrentMonthDay = date.Month == _calendarMonthDate.Month,
                IsSelected = date.Date == _viewModel.SelectedDate.Date
            });
        }
    }

    private async Task NavigateToExerciseSelectionAsync(DailyWorkoutCardViewModel card)
    {
        if (card == null)
        {
            return;
        }

        var services = Handler?.MauiContext?.Services ?? IPlatformApplication.Current.Services;

        var exerciseSelectionPage = ActivatorUtilities.CreateInstance<ExerciseSelectionPage>(
            services,
            card.WorkoutId ?? Guid.Empty,
            card.WorkoutDate,
            string.IsNullOrWhiteSpace(card.WorkoutName) ? "Antrenman" : card.WorkoutName);

        await Navigation.PushAsync(exerciseSelectionPage);
    }
}

public sealed class CalendarDayItem
{
    public DateTime Date { get; init; }

    public string DayNumber { get; init; } = string.Empty;

    public bool IsCurrentMonthDay { get; init; }

    public bool IsSelected { get; init; }

    
}





