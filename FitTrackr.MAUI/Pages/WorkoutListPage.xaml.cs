using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace FitTrackr.MAUI.Pages;

public partial class WorkoutListPage : ContentPage
{
    private readonly WorkoutListViewModel _viewModel;
    private readonly IServiceProvider serviceProvider;
    private DateTime _calendarMonthDate;
    private bool _isCalendarPanelVisible;
    private CancellationTokenSource _monthPreloadCts;
    private (int year, int month)? _prevCachedMonth;
    private (int year, int month)? _currCachedMonth;
    private (int year, int month)? _nextCachedMonth;
    private bool _calendarPreMeasured;

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

        PreloadInitialMonth();
    }

    private void PreloadInitialMonth()
    {
        var year = _calendarMonthDate.Year;
        var month = _calendarMonthDate.Month;

        var days = GenerateCalendarDays(year, month);
        foreach (var day in days)
        {
            CalendarDays.Add(day);
        }

        _currCachedMonth = (year, month);

        _monthPreloadCts?.Cancel();
        _monthPreloadCts = new CancellationTokenSource();
        _ = PreloadAdjacentMonthsAsync(_monthPreloadCts.Token);
    }

    private async Task PreloadAdjacentMonthsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;

                var prevMonth = _calendarMonthDate.AddMonths(-1);
                var prevKey = (prevMonth.Year, prevMonth.Month);
                if (_prevCachedMonth != prevKey)
                {
                    GenerateCalendarDays(prevMonth.Year, prevMonth.Month);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _prevCachedMonth = prevKey;
                    }
                }

                if (cancellationToken.IsCancellationRequested) return;

                var nextMonth = _calendarMonthDate.AddMonths(1);
                var nextKey = (nextMonth.Year, nextMonth.Month);
                if (_nextCachedMonth != nextKey)
                {
                    GenerateCalendarDays(nextMonth.Year, nextMonth.Month);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _nextCachedMonth = nextKey;
                    }
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await RefreshWorkoutDataAsync();
            _ = EnsureCalendarPrelayoutAsync();
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

    private async Task EnsureCalendarPrelayoutAsync()
    {
        if (_calendarPreMeasured) return;

        await Task.Delay(50);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var grid = this.Content as Grid;
                if (grid != null)
                {
                    grid.InvalidateMeasure();
                }
                _calendarPreMeasured = true;
            }
            catch
            {
            }
        });
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
        IsCalendarPanelVisible = !IsCalendarPanelVisible;
    }

    private void OnPrevCalendarMonthClicked(object sender, EventArgs e)
    {
        _calendarMonthDate = _calendarMonthDate.AddMonths(-1);
        OnPropertyChanged(nameof(CalendarMonthTitle));
        LoadMonthFast(_calendarMonthDate.Year, _calendarMonthDate.Month);
    }

    private void OnNextCalendarMonthClicked(object sender, EventArgs e)
    {
        _calendarMonthDate = _calendarMonthDate.AddMonths(1);
        OnPropertyChanged(nameof(CalendarMonthTitle));
        LoadMonthFast(_calendarMonthDate.Year, _calendarMonthDate.Month);
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
        LoadMonthFast(_calendarMonthDate.Year, _calendarMonthDate.Month);
    }

    private void LoadMonthFast(int year, int month)
    {
        var key = (year, month);

        if (_currCachedMonth == key)
        {
            UpdateSelectionOnly();
            return;
        }

        _monthPreloadCts?.Cancel();
        _monthPreloadCts = new CancellationTokenSource();

        var cancellationToken = _monthPreloadCts.Token;

        _ = Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;

            var newDays = GenerateCalendarDays(year, month);

            if (cancellationToken.IsCancellationRequested) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (_calendarMonthDate.Year != year || _calendarMonthDate.Month != month) return;

                UpdateCalendarDaysInPlace(newDays);
                _currCachedMonth = key;

                PreloadAdjacentAsync(cancellationToken);
            });
        }, cancellationToken);
    }

    private void PreloadAdjacentAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;

            var prevMonth = _calendarMonthDate.AddMonths(-1);
            var prevKey = (prevMonth.Year, prevMonth.Month);
            if (_prevCachedMonth != prevKey)
            {
                GenerateCalendarDays(prevMonth.Year, prevMonth.Month);
                if (!cancellationToken.IsCancellationRequested)
                    _prevCachedMonth = prevKey;
            }

            if (cancellationToken.IsCancellationRequested) return;

            var nextMonth = _calendarMonthDate.AddMonths(1);
            var nextKey = (nextMonth.Year, nextMonth.Month);
            if (_nextCachedMonth != nextKey)
            {
                GenerateCalendarDays(nextMonth.Year, nextMonth.Month);
                if (!cancellationToken.IsCancellationRequested)
                    _nextCachedMonth = nextKey;
            }
        }, cancellationToken);
    }

    private void UpdateSelectionOnly()
    {
        if (CalendarDays.Count == 0) return;

        var selectedDate = _viewModel.SelectedDate.Date;
        foreach (var day in CalendarDays)
        {
            var wasSelected = day.IsSelected;
            day.IsSelected = day.Date.Date == selectedDate;

            if (wasSelected != day.IsSelected)
            {
                day.OnPropertyChanged(nameof(CalendarDayItem.IsSelected));
            }
        }
    }

    private void UpdateCalendarDaysInPlace(List<CalendarDayItem> newDays)
    {
        if (CalendarDays.Count != newDays.Count) return;

        var selectedDate = _viewModel.SelectedDate.Date;

        for (int i = 0; i < CalendarDays.Count; i++)
        {
            var existingDay = CalendarDays[i];
            var newDay = newDays[i];

            var dateChanged = existingDay.Date != newDay.Date;
            var dayNumberChanged = existingDay.DayNumber != newDay.DayNumber;
            var isCurrentMonthChanged = existingDay.IsCurrentMonthDay != newDay.IsCurrentMonthDay;
            var wasSelected = existingDay.IsSelected;
            var isNowSelected = newDay.Date.Date == selectedDate;
            var isSelectedChanged = wasSelected != isNowSelected;

            if (!dateChanged && !dayNumberChanged && !isCurrentMonthChanged && !isSelectedChanged)
                continue;

            if (dateChanged)
                existingDay.Date = newDay.Date;
            if (dayNumberChanged)
                existingDay.DayNumber = newDay.DayNumber;
            if (isCurrentMonthChanged)
                existingDay.IsCurrentMonthDay = newDay.IsCurrentMonthDay;
            if (isSelectedChanged)
                existingDay.IsSelected = isNowSelected;

            if (dateChanged || dayNumberChanged || isCurrentMonthChanged || isSelectedChanged)
            {
                if (dateChanged)
                    existingDay.OnPropertyChanged(nameof(CalendarDayItem.Date));
                if (dayNumberChanged)
                    existingDay.OnPropertyChanged(nameof(CalendarDayItem.DayNumber));
                if (isCurrentMonthChanged)
                    existingDay.OnPropertyChanged(nameof(CalendarDayItem.IsCurrentMonthDay));
                if (isSelectedChanged)
                    existingDay.OnPropertyChanged(nameof(CalendarDayItem.IsSelected));
            }
        }
    }

    private List<CalendarDayItem> GenerateCalendarDays(int year, int month)
    {
        var days = new List<CalendarDayItem>(42);
        var firstDayOfMonth = new DateTime(year, month, 1);
        var startOffset = (7 + (firstDayOfMonth.DayOfWeek - DayOfWeek.Monday)) % 7;
        var gridStart = firstDayOfMonth.AddDays(-startOffset);

        var selectedDate = _viewModel.SelectedDate.Date;

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            days.Add(new CalendarDayItem
            {
                Date = date,
                DayNumber = date.Day.ToString(CultureInfo.InvariantCulture),
                IsCurrentMonthDay = date.Month == month,
                IsSelected = date.Date == selectedDate
            });
        }

        return days;
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

public sealed class CalendarDayItem : INotifyPropertyChanged
{
    private DateTime _date;
    private string _dayNumber = string.Empty;
    private bool _isCurrentMonthDay;
    private bool _isSelected;

    public DateTime Date
    {
        get => _date;
        set
        {
            if (_date != value)
            {
                _date = value;
                OnPropertyChanged(nameof(Date));
            }
        }
    }

    public string DayNumber
    {
        get => _dayNumber;
        set
        {
            if (_dayNumber != value)
            {
                _dayNumber = value;
                OnPropertyChanged(nameof(DayNumber));
            }
        }
    }

    public bool IsCurrentMonthDay
    {
        get => _isCurrentMonthDay;
        set
        {
            if (_isCurrentMonthDay != value)
            {
                _isCurrentMonthDay = value;
                OnPropertyChanged(nameof(IsCurrentMonthDay));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
