using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Localization;
using FitTrackr.MAUI.Pages;
using FitTrackr.MAUI.Services;
using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class ProfileViewModel : ObservableObject
    {
        private const string AvatarFolderName = "profile";
        private const string AvatarFileBaseName = "avatar";

        private readonly AuthService authService;
        private string username = string.Empty;
        private string heightCm = string.Empty;
        private string weightKg = string.Empty;
        private string selectedGender = string.Empty;
        private string selectedGoal = string.Empty;
        private bool hasAvatarImage;
        private ImageSource? avatarImageSource;
        private byte[]? avatarImageBytes;
        private bool hasLoadedProfile = false;

        // Not: bu değerler API/Preferences'ta saklanan veridir, UI diline göre çevrilmez —
        // aksi halde dil değişince kullanıcının kayıtlı seçimi listede bulunamaz.
        public IReadOnlyList<string> GenderOptions { get; } = new[] { "Erkek", "Kadın", "Diğer" };
        public IReadOnlyList<string> GoalOptions { get; } = new[] { "Kas Yapma", "Keskinleşme", "Koruma" };

        public string Username
        {
            get => username;
            set
            {
                if (SetProperty(ref username, value))
                {
                    OnPropertyChanged(nameof(AvatarInitials));
                }
            }
        }

        public string HeightCm
        {
            get => heightCm;
            set => SetProperty(ref heightCm, value);
        }

        public string WeightKg
        {
            get => weightKg;
            set => SetProperty(ref weightKg, value);
        }

        public string SelectedGender
        {
            get => selectedGender;
            set => SetProperty(ref selectedGender, value);
        }

        public string SelectedGoal
        {
            get => selectedGoal;
            set => SetProperty(ref selectedGoal, value);
        }

        public bool HasAvatarImage
        {
            get => hasAvatarImage;
            set
            {
                if (SetProperty(ref hasAvatarImage, value))
                {
                    OnPropertyChanged(nameof(HasNoAvatarImage));
                    OnPropertyChanged(nameof(AvatarActionText));
                }
            }
        }

        public bool HasNoAvatarImage => !HasAvatarImage;

        public ImageSource? AvatarImageSource
        {
            get => avatarImageSource;
            set => SetProperty(ref avatarImageSource, value);
        }

        public string AvatarInitials => string.IsNullOrWhiteSpace(Username)
            ? "?"
            : Username.Trim()[0].ToString().ToUpperInvariant();

        public string AvatarActionText => HasAvatarImage
            ? LocalizationResourceManager.Instance["Profile_ChangePhoto"]
            : LocalizationResourceManager.Instance["Profile_AddPhoto"];

        public bool IsTurkishSelected => LocalizationResourceManager.Instance.CurrentCulture.TwoLetterISOLanguageName != "en";
        public bool IsEnglishSelected => !IsTurkishSelected;

        public ProfileViewModel(AuthService authService)
        {
            this.authService = authService;
            LocalizationResourceManager.Instance.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(AvatarActionText));
                OnPropertyChanged(nameof(IsTurkishSelected));
                OnPropertyChanged(nameof(IsEnglishSelected));
            };
        }

        [RelayCommand]
        private async Task SetLanguageAsync(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return;

            Preferences.Set("app_language", languageCode);
            var culture = CultureInfo.GetCultureInfo(languageCode == "en" ? "en-US" : "tr-TR");
            CultureInfo.CurrentCulture = culture;
            LocalizationResourceManager.Instance.SetCulture(culture);

            // Sayfa içi Label/Button metinleri {loc:Translate} bağlamasıyla anında güncellenir,
            // ancak Android'in native alt tab bar'ı (Shell TabBar) ilk oluşturulduktan sonra
            // başlık değişikliklerini yansıtmıyor (bilinen MAUI Shell sınırlaması). Bu yüzden
            // Shell'i DI üzerinden yeniden oluşturup aynı sayfaya geri dönüyoruz.
            var newShell = IPlatformApplication.Current!.Services.GetService<AppShell>();
            if (newShell is not null)
            {
                Application.Current!.MainPage = newShell;
                await Shell.Current.GoToAsync("//ProfilePage");
            }
        }

        public async Task LoadProfileAsync()
        {
            Debug.WriteLine("[ProfileVM] LoadProfileAsync started");

            // Eğer zaten yüklediyse ve reset edilmediyse, skip et
            if (hasLoadedProfile)
            {
                Debug.WriteLine("[ProfileVM] Profile already loaded, skipping");
                return;
            }

            // Preferences'tan yükle (EditProfilePage'den kayıtlı veriler)
            LoadFromPreferences();
            Debug.WriteLine($"[ProfileVM] Loaded from Preferences: Username={Username}, Height={HeightCm}, Weight={WeightKg}, Gender={SelectedGender}, Goal={SelectedGoal}");

            // API'den senkronize et
            var apiProfile = await authService.GetProfileAsync();
            if (apiProfile is not null)
            {
                Debug.WriteLine($"[ProfileVM] Got API profile: Username={apiProfile.Username}, Height={apiProfile.HeightCm}, Weight={apiProfile.WeightKg}, Gender={apiProfile.Gender}, Goal={apiProfile.Goal}");

                // Kullanıcı adı: Preferences'ta dolu bir değer varsa (kullanıcının değiştirdiği)
                // onu koru; yoksa API'den gelen değeri kullan.
                // Bu sayede kullanıcı Türkçe karakterli isim kaydettiğinde API'nin eski
                // değeri ezlemesi önlenir.
                if (!string.IsNullOrWhiteSpace(apiProfile.Username) && string.IsNullOrWhiteSpace(Username))
                {
                    Username = apiProfile.Username;
                }

                if (apiProfile.HeightCm.HasValue)
                {
                    HeightCm = apiProfile.HeightCm.ToString() ?? HeightCm;
                }

                if (apiProfile.WeightKg.HasValue)
                {
                    WeightKg = apiProfile.WeightKg.ToString() ?? WeightKg;
                }

                if (!string.IsNullOrWhiteSpace(apiProfile.Gender))
                {
                    SelectedGender = apiProfile.Gender;
                }

                if (!string.IsNullOrWhiteSpace(apiProfile.Goal))
                {
                    SelectedGoal = apiProfile.Goal;
                }

                SaveToPreferences();
                Debug.WriteLine($"[ProfileVM] Saved API data to Preferences");
            }
            else
            {
                Debug.WriteLine("[ProfileVM] API profile is null");
            }

            // Default values - JWT fallback YOK!
            HeightCm = string.IsNullOrWhiteSpace(HeightCm) ? "175" : HeightCm;
            WeightKg = string.IsNullOrWhiteSpace(WeightKg) ? "75" : WeightKg;
            SelectedGender = string.IsNullOrWhiteSpace(SelectedGender) ? GenderOptions[2] : SelectedGender;
            SelectedGoal = string.IsNullOrWhiteSpace(SelectedGoal) ? GoalOptions[2] : SelectedGoal;
            Username = string.IsNullOrWhiteSpace(Username) ? LocalizationResourceManager.Instance["Profile_DefaultUsername"] : Username;

            SaveToPreferences();
            await LoadAvatarFromLocalStorageAsync();

            hasLoadedProfile = true;

            Debug.WriteLine($"[ProfileVM] LoadProfileAsync finished: Username={Username}, Height={HeightCm}, Weight={WeightKg}, Gender={SelectedGender}, Goal={SelectedGoal}");
        }

        [RelayCommand]
        private async Task OpenEditProfileAsync()
        {
            Debug.WriteLine("[ProfileVM] OpenEditProfileCommand called");
            var route = $"{nameof(EditProfilePage)}?username={Uri.EscapeDataString(Username ?? string.Empty)}&height={Uri.EscapeDataString(HeightCm ?? string.Empty)}&weight={Uri.EscapeDataString(WeightKg ?? string.Empty)}&gender={Uri.EscapeDataString(SelectedGender ?? string.Empty)}&goal={Uri.EscapeDataString(SelectedGoal ?? string.Empty)}";
            Debug.WriteLine($"[ProfileVM] Route: {route}");
            await Shell.Current.GoToAsync(route);
        }

        [RelayCommand]
        private async Task SelectAvatarAsync()
        {
            try
            {
                var loc = LocalizationResourceManager.Instance;
                var chooseFromGallery = loc["Profile_ChooseFromGallery"];
                var takePhoto = loc["Profile_TakePhoto"];
                var removePhoto = loc["Profile_RemovePhoto"];

                var action = await Application.Current!.MainPage!.DisplayActionSheet(
                    loc["Profile_PhotoActionSheetTitle"],
                    loc["Profile_Decline"],
                    null,
                    chooseFromGallery,
                    takePhoto,
                    removePhoto);

                if (action == chooseFromGallery)
                    await PickAvatarFromGalleryAsync();
                else if (action == takePhoto)
                    await CaptureAvatarAsync();
                else if (action == removePhoto)
                    await RemoveAvatarAsync();
            }
            catch
            {
                await ShowAlertAsync(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Profile_PhotoSelectFailed"]);
            }
        }

        [RelayCommand]
        private async Task LogoutAsync()
        {
            authService.Logout();
            Application.Current!.MainPage = new NavigationPage(IPlatformApplication.Current.Services.GetService<LoginPage>());
        }

        [RelayCommand]
        public async Task ConfirmDeleteAccountAsync()
        {
            var loc = LocalizationResourceManager.Instance;
            var isConfirmed = await Application.Current!.MainPage!.DisplayAlert(
                loc["Profile_DeleteAccount"],
                loc["Profile_DeleteAccountConfirmMessage"],
                loc["Profile_DeleteAccountConfirmYes"],
                loc["Profile_Decline"]);

            if (!isConfirmed)
            {
                return;
            }

            var success = await authService.DeleteAccountAsync();
            if (success)
            {
                await Application.Current!.MainPage!.DisplayAlert(loc["Profile_DeleteSuccessTitle"], loc["Profile_DeleteSuccessMessage"], loc["Common_OK"]);
                authService.Logout();
                Application.Current.MainPage = new NavigationPage(IPlatformApplication.Current.Services.GetService<LoginPage>());
                return;
            }

            await Application.Current!.MainPage!.DisplayAlert(loc["Common_Error"], loc["Profile_DeleteErrorMessage"], loc["Common_OK"]);
        }

        private void LoadFromPreferences()
        {
            Username = Preferences.Get("username", string.Empty);
            HeightCm = Preferences.Get("height", string.Empty);
            WeightKg = Preferences.Get("weight", string.Empty);
            SelectedGender = Preferences.Get("gender", string.Empty);
            SelectedGoal = Preferences.Get("goal", string.Empty);
        }

        private void SaveToPreferences()
        {
            Preferences.Set("username", Username ?? string.Empty);
            Preferences.Set("height", HeightCm ?? string.Empty);
            Preferences.Set("weight", WeightKg ?? string.Empty);
            Preferences.Set("gender", SelectedGender ?? string.Empty);
            Preferences.Set("goal", SelectedGoal ?? string.Empty);
        }

        public void ResetProfileState()
        {
            Debug.WriteLine("[ProfileVM] ResetProfileState called");
            hasLoadedProfile = false;
        }

        private async Task CaptureAvatarAsync()
        {
            try
            {
                var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (cameraStatus != PermissionStatus.Granted)
                {
                    cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                }

                if (cameraStatus != PermissionStatus.Granted)
                {
                    await ShowAlertAsync(LocalizationResourceManager.Instance["Profile_CameraPermissionTitle"], LocalizationResourceManager.Instance["Profile_CameraPermissionMessage"]);
                    return;
                }

                var photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = LocalizationResourceManager.Instance["Profile_CameraPhotoTitle"] });
                if (photo is null)
                {
                    return;
                }

                await SaveAndApplyAvatarAsync(photo);
            }
            catch
            {
                await ShowAlertAsync(LocalizationResourceManager.Instance["Profile_CameraUnavailableTitle"], LocalizationResourceManager.Instance["Profile_CameraUnavailableMessage"]);
            }
        }

        private async Task PickAvatarFromGalleryAsync()
        {
            try
            {
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    var photosStatus = await Permissions.CheckStatusAsync<Permissions.Photos>();
                    if (photosStatus != PermissionStatus.Granted)
                    {
                        photosStatus = await Permissions.RequestAsync<Permissions.Photos>();
                    }

                    if (photosStatus != PermissionStatus.Granted)
                    {
                        await ShowAlertAsync(LocalizationResourceManager.Instance["Profile_GalleryPermissionTitle"], LocalizationResourceManager.Instance["Profile_GalleryPermissionMessage"]);
                        return;
                    }
                }

                var photo = await MediaPicker.Default.PickPhotoAsync();
                if (photo is null)
                {
                    return;
                }

                await SaveAndApplyAvatarAsync(photo);
            }
            catch
            {
                await ShowAlertAsync(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Profile_GalleryErrorMessage"]);
            }
        }

        private async Task SaveAndApplyAvatarAsync(FileResult photo)
        {
            await using var inputStream = await photo.OpenReadAsync();
            await using var memoryStream = new MemoryStream();
            await inputStream.CopyToAsync(memoryStream);

            var imageBytes = memoryStream.ToArray();
            var extension = Path.GetExtension(photo.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            await SaveAvatarToLocalStorageAsync(imageBytes, extension);
            await ApplyAvatarAsync(imageBytes);
        }

        private static async Task SaveAvatarToLocalStorageAsync(byte[] imageBytes, string extension)
        {
            var avatarDirectory = Path.Combine(FileSystem.AppDataDirectory, AvatarFolderName);
            Directory.CreateDirectory(avatarDirectory);

            foreach (var existingFile in Directory.GetFiles(avatarDirectory, $"{AvatarFileBaseName}.*"))
            {
                File.Delete(existingFile);
            }

            var avatarPath = Path.Combine(avatarDirectory, $"{AvatarFileBaseName}{extension}");
            await File.WriteAllBytesAsync(avatarPath, imageBytes);
        }

        private async Task LoadAvatarFromLocalStorageAsync()
        {
            var avatarDirectory = Path.Combine(FileSystem.AppDataDirectory, AvatarFolderName);
            if (!Directory.Exists(avatarDirectory))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    avatarImageBytes = null;
                    AvatarImageSource = null;
                    HasAvatarImage = false;
                });
                return;
            }

            var avatarPath = Directory.GetFiles(avatarDirectory, $"{AvatarFileBaseName}.*").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(avatarPath) || !File.Exists(avatarPath))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    avatarImageBytes = null;
                    AvatarImageSource = null;
                    HasAvatarImage = false;
                });
                return;
            }

            var imageBytes = await File.ReadAllBytesAsync(avatarPath);
            await ApplyAvatarAsync(imageBytes);
        }

        private async Task ApplyAvatarAsync(byte[] imageBytes)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                avatarImageBytes = imageBytes;
                AvatarImageSource = ImageSource.FromStream(() => new MemoryStream(avatarImageBytes));
                HasAvatarImage = true;
            });
        }

        private async Task RemoveAvatarAsync()
        {
            var avatarDirectory = Path.Combine(FileSystem.AppDataDirectory, AvatarFolderName);
            if (Directory.Exists(avatarDirectory))
            {
                foreach (var existingFile in Directory.GetFiles(avatarDirectory, $"{AvatarFileBaseName}.*"))
                {
                    File.Delete(existingFile);
                }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                avatarImageBytes = null;
                AvatarImageSource = null;
                HasAvatarImage = false;
            });
        }

        private static async Task ShowAlertAsync(string title, string message)
        {
            if (Application.Current?.MainPage is not null)
            {
                await Application.Current.MainPage.DisplayAlert(title, message, LocalizationResourceManager.Instance["Common_OK"]);
            }
        }
    }
}
