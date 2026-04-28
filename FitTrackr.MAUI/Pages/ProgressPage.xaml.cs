using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages
{
    public partial class ProgressPage : ContentPage
    {
        private readonly ProgressViewModel _viewModel;
        private bool _isLoaded;
        private bool _isExerciseSelectorOpen;

        public ProgressPage(ProgressViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!_isLoaded)
            {
                _isLoaded = true;
                await _viewModel.InitializeAsync();
                return;
            }

            await _viewModel.RefreshAsync();
        }

        private async void OnExerciseSelectorTapped(object sender, TappedEventArgs e)
        {
            if (_isExerciseSelectorOpen || _viewModel.ExerciseFilters.Count == 0)
            {
                return;
            }

            _isExerciseSelectorOpen = true;
            try
            {
                var options = _viewModel.ExerciseFilters.Select(x => x.Title).ToArray();
                var selected = await DisplayActionSheet("Egzersiz seç", "İptal", null, options);

                if (string.IsNullOrWhiteSpace(selected) || selected == "İptal")
                {
                    return;
                }

                var option = _viewModel.ExerciseFilters.FirstOrDefault(x => x.Title == selected);
                if (option is null || option.Equals(_viewModel.SelectedExercise))
                {
                    return;
                }

                _viewModel.SelectExerciseCommand.Execute(option);
            }
            finally
            {
                _isExerciseSelectorOpen = false;
            }
        }
    }
}
