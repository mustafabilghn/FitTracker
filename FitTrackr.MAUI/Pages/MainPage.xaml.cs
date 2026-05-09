using FitTrackr.MAUI.Services;
using FitTrackr.MAUI.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FitTrackr.MAUI
{
    public partial class MainPage : ContentPage
    {
        private readonly AuthService authService;
        private readonly WorkoutService workoutService;
        private readonly ExerciseService exerciseService;

        private string username = string.Empty;
        private ImageSource? avatarImageSource;
        private bool isInitialized = false;

        public string Username
        {
            get => username;
            set
            {
                if (username != value)
                {
                    username = value;
                    OnPropertyChanged();
                }
            }
        }

        public ImageSource? AvatarImageSource
        {
            get => avatarImageSource;
            set
            {
                if (avatarImageSource != value)
                {
                    avatarImageSource = value;
                    OnPropertyChanged();
                }
            }
        }

        private string benchPressMaxWeight = "0";
        private string squatMaxWeight = "0";
        private string deadliftMaxWeight = "0";
        private string barbellRowMaxWeight = "0";

        public string BenchPressMaxWeight
        {
            get => benchPressMaxWeight;
            set
            {
                if (benchPressMaxWeight != value)
                {
                    benchPressMaxWeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SquatMaxWeight
        {
            get => squatMaxWeight;
            set
            {
                if (squatMaxWeight != value)
                {
                    squatMaxWeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DeadliftMaxWeight
        {
            get => deadliftMaxWeight;
            set
            {
                if (deadliftMaxWeight != value)
                {
                    deadliftMaxWeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BarbellRowMaxWeight
        {
            get => barbellRowMaxWeight;
            set
            {
                if (barbellRowMaxWeight != value)
                {
                    barbellRowMaxWeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public MainPage(WorkoutService workoutService, AuthService authService, ExerciseService exerciseService)
        {
            InitializeComponent();
            BindingContext = this;
            this.authService = authService;
            this.workoutService = workoutService;
            this.exerciseService = exerciseService;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // ✅ Subscribe to FUT stats refresh message (triggered from ProgressViewModel)
            if (!isInitialized)
            {
                WeakReferenceMessenger.Default.Register<FutStatsRefreshMessage>(this, OnFutStatsRefreshRequested);
                isInitialized = true;
            }

            // Load all data
            _ = LoadMainPageDataAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // Unsubscribe when leaving the page to avoid memory leaks
            WeakReferenceMessenger.Default.Unregister<FutStatsRefreshMessage>(this);
            isInitialized = false;
        }

        /// <summary>
        /// ✅ Message handler: Triggered when exercise is added in ProgressViewModel
        /// Refreshes FUT card stats
        /// </summary>
        private async void OnFutStatsRefreshRequested(object recipient, FutStatsRefreshMessage message)
        {
            try
            {
                await LoadFutCardStatsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] OnFutStatsRefreshRequested error: {ex.Message}");
            }
        }

        private async Task LoadMainPageDataAsync()
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                // ✅ 1. Username yükle (Preferences → JWT fallback)
                Username = Preferences.Get("username", string.Empty);
                if (string.IsNullOrWhiteSpace(Username))
                {
                    var token = await authService.GetTokenAsync();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        var handler = new JwtSecurityTokenHandler();
                        var jwt = handler.ReadJwtToken(token);
                        var fullName = jwt.Claims
                            .FirstOrDefault(c =>
                                c.Type == "unique_name" ||
                                c.Type == "email" ||
                                c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
                            ?.Value;

                        Username = fullName?.Split(' ')[0] ?? "Kullanıcı";
                    }
                    else
                    {
                        Username = "Kullanıcı";
                    }
                }

                // ✅ 2. Greeting mesajı güncelle
                GreetingLabel.Text = GetGreetingMessage();

                // ✅ 3. Avatar fresh load et
                AvatarImageSource = LoadAvatarImageSourceFresh();

                // ✅ 4. FUT CARD STATS - Dinamik olarak yükle (BP, SQ, DL, BR)
                await LoadFutCardStatsAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", $"Veriler alınırken bir hata oluştu: {ex.Message}", "Tamam");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private string GetGreetingMessage()
        {
            var hour = DateTime.Now.Hour;
            var greeting = hour switch
            {
                >= 5 and < 12 => "Günaydın",      // 05:00 - 11:59
                >= 12 and < 17 => "İyi günler",   // 12:00 - 16:59
                >= 17 and < 21 => "İyi akşamlar", // 17:00 - 20:59
                _ => "İyi geceler"                 // 21:00 - 04:59
            };

            return $"{greeting}, {Username}";
        }

        /// <summary>
        /// ✅ FİKS: Byte array'den ImageSource oluşturarak cache sorununu çöz
        /// ProfilePage'de avatar değiştirildiğinde, MainPage geri açıldığında 
        /// güncellenmiş avatar gösteriliyor
        /// </summary>
        private ImageSource? LoadAvatarImageSourceFresh()
        {
            var avatarDirectory = Path.Combine(FileSystem.AppDataDirectory, "profile");
            if (!Directory.Exists(avatarDirectory))
            {
                return null;
            }

            var avatarPath = Directory.GetFiles(avatarDirectory, "avatar.*").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(avatarPath) || !File.Exists(avatarPath))
            {
                return null;
            }

            try
            {
                // ✅ Byte array'den oluştur - ImageSource.FromFile() cache'i bypass ediyor
                var imageBytes = File.ReadAllBytes(avatarPath);
                return ImageSource.FromStream(() => new MemoryStream(imageBytes));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// ✅ FUT CARD STATS: Tüm antrenmanları tarayıp her egzersiz için en yüksek ağırlığı bul
        /// 
        /// Mantık:
        /// 1. Tüm workout'ları çek
        /// 2. Her workout'ta exercise'ları dolaş
        /// 3. Exercise adına göre filtrele (Bench Press, Squat, Deadlift, Barbell Row)
        /// 4. Her exercise'ın SetNumber'lara göre sırala ve beste WeightInKg bul
        /// 5. Property'lere ata
        /// 
        /// NOT: ProgressViewModel ile aynı mantık kullanılıyor (best weight detection)
        /// </summary>
        private async Task LoadFutCardStatsAsync()
        {
            try
            {
                // 1. Tüm workout'ları getir
                var workoutSummaries = await workoutService.GetWorkoutsAsync();

                if (workoutSummaries == null || workoutSummaries.Count == 0)
                {
                    // Veri yoksa default "0" kalır
                    return;
                }

                // 2. Her egzersiz kategorisi için en yüksek weight'i sakla
                var benchPressMax = 0.0;
                var squatMax = 0.0;
                var deadliftMax = 0.0;
                var barbellRowMax = 0.0;

                // 3. Tüm workout'ları dolaş
                foreach (var workoutSummary in workoutSummaries)
                {
                    var workout = await workoutService.GetWorkoutByIdAsync(workoutSummary.Id);

                    if (workout?.Exercises == null || workout.Exercises.Count == 0)
                    {
                        continue;
                    }

                    // 4. Her workout'taki exercise'ları dolaş
                    foreach (var exerciseSummary in workout.Exercises)
                    {
                        var exercise = await exerciseService.GetExerciseByIdAsync(exerciseSummary.Id);

                        if (exercise?.ExerciseSets == null || exercise.ExerciseSets.Count == 0)
                        {
                            continue;
                        }

                        // 5. En yüksek WeightInKg'yi bul
                        var maxWeightInSet = exercise.ExerciseSets.Max(s => s.WeightInKg);

                        // 6. Exercise adına göre categorize et
                        var exerciseName = exercise.ExerciseName ?? string.Empty;

                        if (exerciseName.Contains("Bench Press", StringComparison.OrdinalIgnoreCase) ||
                            exerciseName.Contains("Bench", StringComparison.OrdinalIgnoreCase) ||
                            exerciseName.Contains("BP", StringComparison.OrdinalIgnoreCase))
                        {
                            benchPressMax = Math.Max(benchPressMax, maxWeightInSet);
                        }
                        else if (exerciseName.Contains("Squat", StringComparison.OrdinalIgnoreCase) ||
                                 exerciseName.Contains("SQ", StringComparison.OrdinalIgnoreCase))
                        {
                            squatMax = Math.Max(squatMax, maxWeightInSet);
                        }
                        else if (exerciseName.Contains("Deadlift", StringComparison.OrdinalIgnoreCase) ||
                                 exerciseName.Contains("DL", StringComparison.OrdinalIgnoreCase))
                        {
                            deadliftMax = Math.Max(deadliftMax, maxWeightInSet);
                        }
                        else if (exerciseName.Contains("Barbell Row", StringComparison.OrdinalIgnoreCase) ||
                                 exerciseName.Contains("Rows", StringComparison.OrdinalIgnoreCase) ||
                                 exerciseName.Contains("BR", StringComparison.OrdinalIgnoreCase))
                        {
                            barbellRowMax = Math.Max(barbellRowMax, maxWeightInSet);
                        }
                    }
                }

                // ✅ UI'ye güncelle
                BenchPressMaxWeight = benchPressMax > 0 ? benchPressMax.ToString("0") : "0";
                SquatMaxWeight = squatMax > 0 ? squatMax.ToString("0") : "0";
                DeadliftMaxWeight = deadliftMax > 0 ? deadliftMax.ToString("0") : "0";
                BarbellRowMaxWeight = barbellRowMax > 0 ? barbellRowMax.ToString("0") : "0";

                System.Diagnostics.Debug.WriteLine($"[MainPage] FUT Stats Loaded: BP={BenchPressMaxWeight}, SQ={SquatMaxWeight}, DL={DeadliftMaxWeight}, BR={BarbellRowMaxWeight}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] LoadFutCardStatsAsync error: {ex.Message}");
                // Hata durumunda property'ler default "0" kalır
            }
        }
    }
}
