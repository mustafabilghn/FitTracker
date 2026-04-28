$root = 'C:\Users\musta\source\repos\FitTracker'

Set-Content -Path (Join-Path $root 'FitTrackr.MAUI\ViewModels\WorkoutListViewModel.cs') -Value @'
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

namespace FitTrackr.MAUI.ViewModels
{
    public class WorkoutListViewModel : ObservableObject
    {
        private readonly WorkoutService _workoutService;
        private readonly ExerciseService _exerciseService;
        private readonly List<WorkoutSummaryDto> _allWorkouts = new();

        private DateTime _selectedDate;
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (SetProperty(ref _selectedDate, value.Date))
                {
                    OnPropertyChanged(nameof(SelectedDateDisplay));
                    BuildWeekStrip();
                    _ = RefreshDailyWorkoutCardAsync();
                }
            }
        }

        private static readonly CultureInfo TurkishCulture = new("tr-TR");

        public string SelectedDateDisplay
        {
            get
            {
                var today = DateTime.Today;
                var selected = SelectedDate.Date;

                if (selected == today)
                    return "Bugün";

                if (selected == today.AddDays(-1))
                    return "Dün";

                if (selected == today.AddDays(1))
                    return "Yarın";

                return selected.ToString("dd MMMM dddd", TurkishCulture);
            }
        }

        public ObservableCollection<WeekDayItem> WeekDays { get; } = new();
        public ObservableCollection<DailyWorkoutCardViewModel> DailyWorkouts { get; } = new();

        public ICommand SelectDateCommand { get; }
        public ICommand GoToTodayCommand { get; }
        public ICommand ToggleWorkoutExpandCommand { get; }
        public IAsyncRelayCommand<DailyWorkoutCardViewModel> RenameWorkoutCommand { get; }
        public IAsyncRelayCommand<WorkoutExerciseItemViewModel> DeleteExerciseCommand { get; }

        public WorkoutListViewModel(WorkoutService workoutService, ExerciseService exerciseService)
        {
            _workoutService = workoutService;
            _exerciseService = exerciseService;

            SelectDateCommand = new RelayCommand<WeekDayItem>(OnDateSelected);
            GoToTodayCommand = new RelayCommand(() => SelectedDate = DateTime.Today);
            ToggleWorkoutExpandCommand = new RelayCommand<DailyWorkoutCardViewModel>(ToggleCardExpansion);
            RenameWorkoutCommand = new AsyncRelayCommand<DailyWorkoutCardViewModel>(RenameWorkoutAsync);
            DeleteExerciseCommand = new AsyncRelayCommand<WorkoutExerciseItemViewModel>(DeleteExerciseAsync);

            SelectedDate = DateTime.Today;
        }

        public async Task LoadWorkoutsAsync()
        {
            _allWorkouts.Clear();

            var workouts = await _workoutService.GetWorkoutsAsync();
            _allWorkouts.AddRange(workouts);

            BuildWeekStrip();
            await RefreshDailyWorkoutCardAsync();
        }

        public bool HasWorkoutOnDate(DateTime date)
        {
            return _allWorkouts.Any(w => w.WorkoutDate.Date == date.Date);
        }

        private void OnDateSelected(WeekDayItem? day)
        {
            if (day == null)
                return;

            SelectedDate = day.Date;
        }

        private void ToggleCardExpansion(DailyWorkoutCardViewModel? card)
        {
            if (card == null || !card.HasExercises)
                return;

            card.IsExpanded = !card.IsExpanded;
        }

        private async Task RenameWorkoutAsync(DailyWorkoutCardViewModel? card)
        {
            if (card == null)
                return;

            var currentName = string.IsNullOrWhiteSpace(card.WorkoutName)
                ? DailyWorkoutCardViewModel.DefaultWorkoutName
                : card.WorkoutName;

            var newName = await Shell.Current.DisplayPromptAsync(
                "Antrenman Adı",
                "Antrenman adını düzenle",
                accept: "Kaydet",
                cancel: "İptal",
                placeholder: "Antrenman adı",
                maxLength: 20,
                keyboard: Keyboard.Text,
                initialValue: currentName);

            if (newName == null)
                return;

            await SaveWorkoutNameAsync(card, newName);
        }

        private async Task DeleteExerciseAsync(WorkoutExerciseItemViewModel? exercise)
        {
            if (exercise == null)
            {
                return;
            }

            var card = DailyWorkouts.FirstOrDefault(w => w.Exercises.Any(e => e.ExerciseId == exercise.ExerciseId));
            if (card == null)
            {
                return;
            }

            try
            {
                await _exerciseService.DeleteExerciseAsync(exercise.ExerciseId);
                card.RemoveExercise(exercise.ExerciseId);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", $"Egzersiz silinemedi: {ex.Message}", "Tamam");
            }
        }

        private async Task SaveWorkoutNameAsync(DailyWorkoutCardViewModel card, string workoutName)
        {
            var normalizedName = string.IsNullOrWhiteSpace(workoutName)
                ? DailyWorkoutCardViewModel.DefaultWorkoutName
                : workoutName.Trim();

            try
            {
                if (card.HasPersistedWorkout && card.WorkoutId.HasValue)
                {
                    var updatedWorkout = await _workoutService.UpdateWorkoutAsync(card.WorkoutId.Value, new UpdateWorkoutRequestDto
                    {
                        WorkoutName = normalizedName,
                        WorkoutDate = card.WorkoutDate
                    });

                    SyncWorkout(updatedWorkout);
                    card.ApplyWorkout(updatedWorkout);
                    BuildWeekStrip();
                    return;
                }

                var createdWorkout = await _workoutService.AddWorkoutAsync(new WorkoutRequestDto
                {
                    WorkoutName = normalizedName,
                    WorkoutDate = card.WorkoutDate,
                    DurationMinutes = 0,
                    LocationId = Guid.Empty
                });

                _allWorkouts.Add(createdWorkout);
                card.ApplyWorkout(createdWorkout);
                card.UpdateExerciseState(false);
                BuildWeekStrip();
                WeakReferenceMessenger.Default.Send(new WorkoutAddedMessage(createdWorkout));
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", $"Antrenman adi guncellenemedi: {ex.Message}", "Tamam");
            }
        }

        private void SyncWorkout(WorkoutSummaryDto workout)
        {
            var existingWorkout = _allWorkouts.FirstOrDefault(w => w.Id == workout.Id);

            if (existingWorkout == null)
            {
                _allWorkouts.Add(workout);
                return;
            }

            existingWorkout.WorkoutName = workout.WorkoutName;
            existingWorkout.WorkoutDate = workout.WorkoutDate;
            existingWorkout.DurationMinutes = workout.DurationMinutes;
            existingWorkout.Location = workout.Location;
        }

        private void BuildWeekStrip()
        {
            var weekStart = GetWeekStart(SelectedDate);
            var culture = new CultureInfo("tr-TR");

            WeekDays.Clear();

            for (var i = 0; i < 7; i++)
            {
                var day = weekStart.AddDays(i);
                WeekDays.Add(new WeekDayItem
                {
                    Date = day,
                    DayLabel = culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek),
                    DayNumber = day.Day.ToString(culture),
                    IsSelected = day.Date == SelectedDate.Date,
                    HasWorkout = HasWorkoutOnDate(day)
                });
            }
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }

        private async Task RefreshDailyWorkoutCardAsync()
        {
            DailyWorkouts.Clear();

            var selectedDate = SelectedDate.Date;
            var workoutForDay = _allWorkouts
                .Where(w => w.WorkoutDate.Date == selectedDate)
                .OrderBy(w => w.WorkoutDate)
                .FirstOrDefault();

            var card = new DailyWorkoutCardViewModel(selectedDate, workoutForDay);
            DailyWorkouts.Add(card);

            if (workoutForDay == null)
            {
                card.SetExercises([]);
                return;
            }

            try
            {
                var workoutDetail = await _workoutService.GetWorkoutByIdAsync(workoutForDay.Id);
                var detailedExercises = new List<ExerciseDto>();

                foreach (var exercise in workoutDetail.Exercises ?? [])
                {
                    try
                    {
                        detailedExercises.Add(await _exerciseService.GetExerciseByIdAsync(exercise.Id));
                    }
                    catch
                    {
                        detailedExercises.Add(exercise);
                    }
                }

                card.SetExercises(detailedExercises);
            }
            catch
            {
                card.SetExercises([]);
            }
        }
    }

    public class WeekDayItem : ObservableObject
    {
        private bool _isSelected;
        private bool _hasWorkout;

        public DateTime Date { get; set; }
        public string DayLabel { get; set; } = string.Empty;
        public string DayNumber { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool HasWorkout
        {
            get => _hasWorkout;
            set => SetProperty(ref _hasWorkout, value);
        }
    }
}
'@

Set-Content -Path (Join-Path $root 'FitTrackr.MAUI\Pages\WorkoutListPage.xaml.cs') -Value @'
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
                IsSelected = date.Date == _viewModel.SelectedDate.Date,
                HasWorkout = _viewModel.HasWorkoutOnDate(date)
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

    public bool HasWorkout { get; init; }
}
'@

Set-Content -Path (Join-Path $root 'FitTrackr.MAUI\Pages\WorkoutListPage.xaml') -Value @'
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage
    x:Name="ThisPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:Class="FitTrackr.MAUI.Pages.WorkoutListPage"
    BackgroundColor="#121212">
    <Grid>
        <ScrollView>
            <VerticalStackLayout Padding="20" Spacing="18">
                <Grid ColumnDefinitions="*,Auto" VerticalOptions="Center">
                    <Label
                        Text="Antrenman Günlüğü"
                        FontSize="30"
                        FontAttributes="Bold"
                        TextColor="White"
                        VerticalOptions="Center" />
                    <Border
                        Grid.Column="1"
                        Margin="0,0,8,0"
                        Stroke="#3A3A3A"
                        StrokeThickness="0"
                        BackgroundColor="#242424"
                        StrokeShape="RoundRectangle 28"
                        WidthRequest="56"
                        HeightRequest="56"
                        Padding="0"
                        VerticalOptions="Center"
                        HorizontalOptions="End">

                        <ImageButton
                            Source="calendar.svg"
                            BackgroundColor="Transparent"
                            WidthRequest="56"
                            HeightRequest="56"
                            Padding="14"
                            Clicked="OnCalendarClicked" />
                    </Border>
                </Grid>

                <Border
                    IsVisible="{Binding Source={x:Reference ThisPage}, Path=IsCalendarPanelVisible}"
                    BackgroundColor="#1F1F1F"
                    Stroke="#333333"
                    StrokeThickness="1"
                    StrokeShape="RoundRectangle 16"
                    Padding="12">
                    <VerticalStackLayout Spacing="10">
                        <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="8">

                            <ImageButton
                                Grid.Column="0"
                                Source="left.svg"
                                Clicked="OnPrevCalendarMonthClicked"
                                BackgroundColor="Transparent"
                                WidthRequest="34"
                                HeightRequest="34"
                                Padding="6"/>

                            <Label
                                Grid.Column="1"
                                Text="{Binding Source={x:Reference ThisPage}, Path=CalendarMonthTitle}"
                                FontSize="16"
                                FontAttributes="Bold"
                                TextColor="White"
                                HorizontalTextAlignment="Center"
                                VerticalTextAlignment="Center" />

                            <ImageButton
                                Grid.Column="2"
                                Source="right.svg"
                                Clicked="OnNextCalendarMonthClicked"
                                BackgroundColor="Transparent"
                                WidthRequest="34"
                                HeightRequest="34"
                                Padding="6"/>
                        </Grid>

                        <Grid ColumnDefinitions="*,*,*,*,*,*,*" ColumnSpacing="4">
                            <Label Text="P" TextColor="#8F8F8F" HorizontalTextAlignment="Center" FontSize="12" />
                            <Label Grid.Column="1" Text="S" TextColor="#8F8F8F" HorizontalTextAlignment="Center" FontSize="12" />
                            <Label Grid.Column="2" Text="C" TextColor="#8F8F8F" HorizontalTextAlignment="Center" FontSize="12" />
                            <Label Grid.Column="3" Text="P" TextColor="#8F8F8F" HorizontalTextAlignment="Center" FontSize="12" />
                            <Label Grid.Column="4" Text="C" TextColor="#8F8F8F" HorizontalTextAlignment="Center" FontSize="12" />
                            <Label Grid.Column="5" Text="C" TextColor="#8F8F8F" HorizontalTextAlignment="Center" FontSize="12" />
                            <Label Grid.Column="6" Text="P" TextColor="#8F8F8F" HorizontalTextAlignment="Center" FontSize="12" />
                        </Grid>

                        <CollectionView
                            ItemsSource="{Binding Source={x:Reference ThisPage}, Path=CalendarDays}"
                            SelectionMode="None"
                            VerticalScrollBarVisibility="Never"
                            HeightRequest="220">
                            <CollectionView.ItemsLayout>
                                <GridItemsLayout Orientation="Vertical" Span="7" HorizontalItemSpacing="4" VerticalItemSpacing="4" />
                            </CollectionView.ItemsLayout>
                            <CollectionView.ItemTemplate>
                                <DataTemplate>
                                    <Border
                                        StrokeThickness="0"
                                        BackgroundColor="Transparent"
                                        StrokeShape="RoundRectangle 10"
                                        HeightRequest="38">
                                        <Border.GestureRecognizers>
                                            <TapGestureRecognizer
                                                Tapped="OnCalendarDayTapped"
                                                CommandParameter="{Binding .}" />
                                        </Border.GestureRecognizers>
                                        <Border.Triggers>
                                            <DataTrigger TargetType="Border" Binding="{Binding IsSelected}" Value="True">
                                                <Setter Property="BackgroundColor" Value="#FF7043" />
                                            </DataTrigger>
                                        </Border.Triggers>
                                        <VerticalStackLayout Spacing="2" HorizontalOptions="Center" VerticalOptions="Center">
                                            <Label
                                                Text="{Binding DayNumber}"
                                                FontSize="13"
                                                HorizontalTextAlignment="Center"
                                                VerticalTextAlignment="Center"
                                                TextColor="#7F7F7F">
                                                <Label.Triggers>
                                                    <DataTrigger TargetType="Label" Binding="{Binding IsCurrentMonthDay}" Value="True">
                                                        <Setter Property="TextColor" Value="#E6E6E6" />
                                                    </DataTrigger>
                                                    <DataTrigger TargetType="Label" Binding="{Binding IsSelected}" Value="True">
                                                        <Setter Property="TextColor" Value="White" />
                                                    </DataTrigger>
                                                </Label.Triggers>
                                            </Label>
                                            <Ellipse
                                                WidthRequest="5"
                                                HeightRequest="5"
                                                Fill="#4CAF50"
                                                IsVisible="{Binding HasWorkout}" />
                                        </VerticalStackLayout>
                                    </Border>
                                </DataTemplate>
                            </CollectionView.ItemTemplate>
                        </CollectionView>
                    </VerticalStackLayout>
                </Border>

                <Grid ColumnDefinitions="*,Auto" VerticalOptions="Center">
                    <Label
                        Text="{Binding SelectedDateDisplay}"
                        FontSize="16"
                        TextColor="#CFCFCF"
                        VerticalOptions="Center" />
                    <Button
                        Grid.Column="1"
                        Text="Bugün"
                        Command="{Binding GoToTodayCommand}"
                        BackgroundColor="#2A2A2A"
                        TextColor="#FF7043"
                        FontAttributes="Bold"
                        CornerRadius="18"
                        Padding="16,8" />
                </Grid>
                <CollectionView
                    ItemsSource="{Binding WeekDays}"
                    SelectionMode="None"
                    HeightRequest="92">
                    <CollectionView.ItemsLayout>
                        <LinearItemsLayout Orientation="Horizontal" ItemSpacing="10" />
                    </CollectionView.ItemsLayout>
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <Border
                                Padding="14,10"
                                StrokeThickness="1"
                                Stroke="#2F2F2F"
                                BackgroundColor="#1B1B1B"
                                StrokeShape="RoundRectangle 16">
                                <Border.GestureRecognizers>
                                    <TapGestureRecognizer
                                        Command="{Binding Source={x:Reference ThisPage}, Path=BindingContext.SelectDateCommand}"
                                        CommandParameter="{Binding .}" />
                                </Border.GestureRecognizers>
                                <Border.Triggers>
                                    <DataTrigger TargetType="Border" Binding="{Binding IsSelected}" Value="True">
                                        <Setter Property="BackgroundColor" Value="#FF7043" />
                                        <Setter Property="Stroke" Value="#FF8A65" />
                                    </DataTrigger>
                                </Border.Triggers>
                                <VerticalStackLayout Spacing="2" HorizontalOptions="Center" VerticalOptions="Center">
                                    <Label
                                        Text="{Binding DayLabel}"
                                        FontSize="12"
                                        HorizontalOptions="Center"
                                        TextColor="#AFAFAF">
                                        <Label.Triggers>
                                            <DataTrigger TargetType="Label" Binding="{Binding IsSelected}" Value="True">
                                                <Setter Property="TextColor" Value="White" />
                                            </DataTrigger>
                                        </Label.Triggers>
                                    </Label>
                                    <Label
                                        Text="{Binding DayNumber}"
                                        FontSize="18"
                                        FontAttributes="Bold"
                                        HorizontalOptions="Center"
                                        TextColor="White" />
                                    <Ellipse
                                        WidthRequest="5"
                                        HeightRequest="5"
                                        Fill="#4CAF50"
                                        IsVisible="{Binding HasWorkout}" />
                                </VerticalStackLayout>
                            </Border>
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                <CollectionView
                    x:Name="WorkoutsCollection"
                    ItemsSource="{Binding DailyWorkouts}"
                    VerticalScrollBarVisibility="Never">
                    <CollectionView.ItemsLayout>
                        <LinearItemsLayout Orientation="Vertical" ItemSpacing="12" />
                    </CollectionView.ItemsLayout>
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <Border
                                Stroke="#2C2C2C"
                                StrokeThickness="1"
                                BackgroundColor="#1E1E1E"
                                StrokeShape="RoundRectangle 18"
                                Padding="18,16">
                                <Border.GestureRecognizers>
                                    <TapGestureRecognizer
                                        Tapped="OnWorkoutCardTapped"
                                        CommandParameter="{Binding .}" />
                                </Border.GestureRecognizers>
                                <VerticalStackLayout Spacing="12">
                                    <Grid ColumnDefinitions="*,Auto" VerticalOptions="Center" ColumnSpacing="14">
                                        <VerticalStackLayout Spacing="3" VerticalOptions="Center">
                                            <Label
                                                Text="{Binding WorkoutName}"
                                                FontSize="18"
                                                FontAttributes="Bold"
                                                TextColor="White"
                                                LineBreakMode="TailTruncation" />
                                        </VerticalStackLayout>
                                        <Grid Grid.Column="1"
                                              ColumnDefinitions="Auto,Auto"
                                              ColumnSpacing="14"
                                              VerticalOptions="Start"
                                              HorizontalOptions="End">
                                            <ImageButton 
                                                Grid.Column="0"
                                                Source="edit.svg"
                                                Command="{Binding Source={x:Reference ThisPage}, Path=BindingContext.RenameWorkoutCommand}"
                                                CommandParameter="{Binding .}"
                                                BackgroundColor="Transparent"
                                                WidthRequest="32"
                                                HeightRequest="32"
                                                Padding="4"
                                                HorizontalOptions="Center"
                                                VerticalOptions="Start"/>
                                            <Grid Grid.Column="1"
                                                  RowDefinitions="Auto,Auto"
                                                  RowSpacing="8"
                                                  HorizontalOptions="Center"
                                                  VerticalOptions="Start">
                                                <ImageButton
                                                    Grid.Row="0"
                                                    Source="add.svg"
                                                    Clicked="OnAddExerciseClicked"
                                                    CommandParameter="{Binding .}"
                                                    BackgroundColor="Transparent"
                                                    WidthRequest="32"
                                                    HeightRequest="32"
                                                    Padding="4"
                                                    HorizontalOptions="Center"
                                                    VerticalOptions="Start"/>
                                                <Grid
                                                    Grid.Row="1"
                                                    IsVisible="{Binding HasExercises}"
                                                    WidthRequest="32"
                                                    HeightRequest="32"
                                                    HorizontalOptions="Center"
                                                    VerticalOptions="Start">
                                                    <Grid.GestureRecognizers>
                                                        <TapGestureRecognizer
                                                            Command="{Binding Source={x:Reference ThisPage}, Path=BindingContext.ToggleWorkoutExpandCommand}"
                                                            CommandParameter="{Binding .}"/>
                                                    </Grid.GestureRecognizers>
                                                    <Image
                                                        Source="down.svg"
                                                        WidthRequest="20"
                                                        HeightRequest="20"
                                                        HorizontalOptions="Center"
                                                        VerticalOptions="Center"
                                                        Aspect="AspectFit">
                                                        <Image.Triggers>
                                                            <DataTrigger TargetType="Image" Binding="{Binding IsExpanded}" Value="True">
                                                                <Setter Property="Source" Value="up.svg"/>
                                                            </DataTrigger>
                                                            <DataTrigger TargetType="Image" Binding="{Binding IsExpanded}" Value="False">
                                                                <Setter Property="Source" Value="down.svg"/>
                                                            </DataTrigger>
                                                        </Image.Triggers>
                                                    </Image>
                                                </Grid>
                                            </Grid>
                                        </Grid>
                                    </Grid>
                                    <Border
                                        IsVisible="{Binding IsExpanded}"
                                        BackgroundColor="#252525"
                                        Stroke="#333333"
                                        StrokeThickness="1"
                                        StrokeShape="RoundRectangle 12"
                                        Padding="8,10">
                                        <CollectionView
                                            ItemsSource="{Binding Exercises}"
                                            SelectionMode="None"
                                            VerticalScrollBarVisibility="Never">
                                            <CollectionView.ItemsLayout>
                                                <LinearItemsLayout Orientation="Vertical" ItemSpacing="8" />
                                            </CollectionView.ItemsLayout>
                                            <CollectionView.ItemTemplate>
                                                <DataTemplate>
                                                    <SwipeView>
                                                        <SwipeView.RightItems>
                                                            <SwipeItems Mode="Execute" SwipeBehaviorOnInvoked="Close">
                                                                <SwipeItem
                                                                    Text="Sil"
                                                                    BackgroundColor="#D84343"
                                                                    Command="{Binding Source={x:Reference ThisPage}, Path=BindingContext.DeleteExerciseCommand}"
                                                                    CommandParameter="{Binding .}" />
                                                            </SwipeItems>
                                                        </SwipeView.RightItems>
                                                        <Border
                                                            BackgroundColor="#1F1F1F"
                                                            Stroke="#343434"
                                                            StrokeThickness="1"
                                                            StrokeShape="RoundRectangle 10"
                                                            Padding="12,10">
                                                            <Border.GestureRecognizers>
                                                                <TapGestureRecognizer
                                                                    Tapped="OnExistingExerciseTapped"
                                                                    CommandParameter="{Binding .}" />
                                                            </Border.GestureRecognizers>
                                                            <VerticalStackLayout Spacing="6">
                                                                <Label
                                                                    Text="{Binding ExerciseName}"
                                                                    FontSize="16"
                                                                    FontAttributes="Bold"
                                                                    TextColor="#F2F2F2" />
                                                                <CollectionView
                                                                    ItemsSource="{Binding Sets}"
                                                                    SelectionMode="None"
                                                                    VerticalScrollBarVisibility="Never">
                                                                    <CollectionView.ItemsLayout>
                                                                        <LinearItemsLayout Orientation="Vertical" ItemSpacing="2" />
                                                                    </CollectionView.ItemsLayout>
                                                                    <CollectionView.ItemTemplate>
                                                                        <DataTemplate>
                                                                            <Label
                                                                                Text="{Binding SetLine}"
                                                                                FontSize="13"
                                                                                TextColor="#B8B8B8"
                                                                                Margin="4,0,0,0" />
                                                                        </DataTemplate>
                                                                    </CollectionView.ItemTemplate>
                                                                </CollectionView>
                                                            </VerticalStackLayout>
                                                        </Border>
                                                    </SwipeView>
                                                </DataTemplate>
                                            </CollectionView.ItemTemplate>
                                        </CollectionView>
                                    </Border>
                                </VerticalStackLayout>
                            </Border>
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                <ActivityIndicator
                    x:Name="LoadingIndicator"
                    IsRunning="False"
                    IsVisible="False"
                    Color="#FF7043"
                    HeightRequest="60"
                    WidthRequest="60"
                    HorizontalOptions="Center"
                    Margin="0,20" />
            </VerticalStackLayout>
        </ScrollView>
    </Grid>
</ContentPage>
'@