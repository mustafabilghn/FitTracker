using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class EditProfileViewModel : ObservableObject
    {
        private readonly AuthService authService;
        private bool isSaving = false;

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string heightCm = string.Empty;

        [ObservableProperty]
        private string weightKg = string.Empty;

        [ObservableProperty]
        private string selectedGender = string.Empty;

        [ObservableProperty]
        private string selectedGoal = string.Empty;

        // Not: ProfileViewModel.GenderOptions/GoalOptions ile aynı sebepten UI diline göre çevrilmez.
        public ObservableCollection<string> GenderOptions { get; } = new() { "Erkek", "Kadın", "Diğer" };
        public ObservableCollection<string> GoalOptions { get; } = new() { "Kas Yapma", "Keskinleşme", "Koruma" };

        public EditProfileViewModel(AuthService authService)
        {
            this.authService = authService;
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            Debug.WriteLine("[EditProfileVM] ApplyQueryAttributes called");

            if (query.TryGetValue("username", out var u))
                Username = Uri.UnescapeDataString(u?.ToString() ?? string.Empty);

            if (query.TryGetValue("height", out var h))
                HeightCm = Uri.UnescapeDataString(h?.ToString() ?? string.Empty);

            if (query.TryGetValue("weight", out var w))
                WeightKg = Uri.UnescapeDataString(w?.ToString() ?? string.Empty);

            if (query.TryGetValue("gender", out var g))
                SelectedGender = Uri.UnescapeDataString(g?.ToString() ?? string.Empty);

            if (query.TryGetValue("goal", out var go))
                SelectedGoal = Uri.UnescapeDataString(go?.ToString() ?? string.Empty);

            Debug.WriteLine($"[EditProfileVM] Loaded: Username={Username}, Height={HeightCm}, Weight={WeightKg}, Gender={SelectedGender}, Goal={SelectedGoal}");
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            Debug.WriteLine("[EditProfileVM] SaveCommand started");

            // Çift tıklamayı önle
            if (isSaving)
            {
                Debug.WriteLine("[EditProfileVM] Already saving, ignoring duplicate click");
                return;
            }

            isSaving = true;

            try
            {
                // Validasyon
                if (string.IsNullOrWhiteSpace(Username))
                {
                    Debug.WriteLine("[EditProfileVM] Validation failed: Username is empty");
                    await ShowAlertAsync("Warning", "Username cannot be empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(HeightCm) || !int.TryParse(HeightCm, out var heightValue))
                {
                    Debug.WriteLine("[EditProfileVM] Validation failed: Invalid height");
                    await ShowAlertAsync("Warning", "Height must be a valid number.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(WeightKg) || !int.TryParse(WeightKg, out var weightValue))
                {
                    Debug.WriteLine("[EditProfileVM] Validation failed: Invalid weight");
                    await ShowAlertAsync("Warning", "Weight must be a valid number.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedGender))
                {
                    Debug.WriteLine("[EditProfileVM] Validation failed: Gender is empty");
                    await ShowAlertAsync("Warning", "Please select a gender.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedGoal))
                {
                    Debug.WriteLine("[EditProfileVM] Validation failed: Goal is empty");
                    await ShowAlertAsync("Warning", "Please select a goal.");
                    return;
                }

                Debug.WriteLine("[EditProfileVM] Validation passed");

                // API isteği oluştur
                var request = new UpdateProfileRequestDto
                {
                    Username = Username.Trim(),
                    HeightCm = heightValue,
                    WeightKg = weightValue,
                    Gender = SelectedGender.Trim(),
                    Goal = SelectedGoal.Trim()
                };

                Debug.WriteLine($"[EditProfileVM] Calling API with: Username={request.Username}, Height={request.HeightCm}, Weight={request.WeightKg}, Gender={request.Gender}, Goal={request.Goal}");

                // API'ye gönder
                var result = await authService.UpdateProfileAsync(request);

                Debug.WriteLine($"[EditProfileVM] API response: {result}");

                // ✅ FİKS: API başarılı olsa da olmasa da Preferences'a kaydet
                // Backend'de profile endpoint'i olmadığından, local Preferences'a kaydediyoruz
                Debug.WriteLine("[EditProfileVM] Saving to local Preferences (API result: " + result + ")");

                Preferences.Set("username", request.Username);
                Preferences.Set("height", HeightCm.Trim());
                Preferences.Set("weight", WeightKg.Trim());
                Preferences.Set("gender", request.Gender);
                Preferences.Set("goal", request.Goal);

                Debug.WriteLine($"[EditProfileVM] Saved to Preferences: Username={request.Username}, Height={HeightCm}, Weight={WeightKg}, Gender={request.Gender}, Goal={request.Goal}");

                // Alert gösterme, direkt geri dön
                // ProfilePage'in OnAppearing'i çalışacak ve yeni verileri yükleyecek
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    Debug.WriteLine("[EditProfileVM] Navigating back");
                    await Shell.Current.GoToAsync("..");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EditProfileVM] SaveAsync error: {ex.Message}\n{ex.StackTrace}");
                await ShowAlertAsync("Error", $"Unexpected error: {ex.Message}");
            }
            finally
            {
                isSaving = false;
            }
        }

        private static async Task ShowAlertAsync(string title, string message)
        {
            if (Application.Current?.MainPage is not null)
            {
                await Application.Current.MainPage.DisplayAlert(title, message, "OK");
            }
        }
    }
}
