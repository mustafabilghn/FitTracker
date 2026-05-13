using FitTrackr.MAUI.Services;
using FitTrackr.MAUI.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Collections.ObjectModel;
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
                    OnPropertyChanged(nameof(FutCardName));
                }
            }
        }

        /// <summary>
        /// FUT kart üzerinde büyük harfle gösterilecek isim.
        /// TextTransform="Uppercase" ve CultureInfo("tr-TR") Android'de i→İ dönüşümünü
        /// yapamadığı için her harf elle dönüştürülüyor.
        /// </summary>
        public string FutCardName => string.IsNullOrEmpty(username)
            ? string.Empty
            : ToTurkishUpper(username);

        private static string ToTurkishUpper(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                sb.Append(c switch
                {
                    'i' => 'İ',   // Türkçe noktalı küçük i → büyük İ
                    'ı' => 'I',   // Türkçe noktasız küçük ı → büyük I
                    _ => char.ToUpperInvariant(c)
                });
            }
            return sb.ToString();
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

        private string totalMaxKg = "0";
        private int streak = 0;
        private string weeklyWorkouts = "0";

        public int Streak
        {
            get => streak;
            set { if (streak != value) { streak = value; OnPropertyChanged(); } }
        }

        /// <summary>Bu haftaki benzersiz antrenman günü sayısı (Pzt–Paz).</summary>
        public string WeeklyWorkouts
        {
            get => weeklyWorkouts;
            set { if (weeklyWorkouts != value) { weeklyWorkouts = value; OnPropertyChanged(); } }
        }

        // Her egzersiz için sabit pozisyon: BP=R1L, SQ=R1R, DL=R2L, BR=R2R, OP=R3L
        private bool hasBP, hasSQ, hasDL, hasBR, hasOP;
        private string bpValue = "0", sqValue = "0", dlValue = "0", brValue = "0", opValue = "0";

        public bool HasBP { get => hasBP; set { if (hasBP != value) { hasBP = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRow1)); } } }
        public bool HasSQ { get => hasSQ; set { if (hasSQ != value) { hasSQ = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRow1)); } } }
        public bool HasDL { get => hasDL; set { if (hasDL != value) { hasDL = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRow2)); } } }
        public bool HasBR { get => hasBR; set { if (hasBR != value) { hasBR = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRow2)); } } }
        public bool HasOP { get => hasOP; set { if (hasOP != value) { hasOP = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRow3)); } } }

        public string BPValue { get => bpValue; set { if (bpValue != value) { bpValue = value; OnPropertyChanged(); } } }
        public string SQValue { get => sqValue; set { if (sqValue != value) { sqValue = value; OnPropertyChanged(); } } }
        public string DLValue { get => dlValue; set { if (dlValue != value) { dlValue = value; OnPropertyChanged(); } } }
        public string BRValue { get => brValue; set { if (brValue != value) { brValue = value; OnPropertyChanged(); } } }
        public string OPValue { get => opValue; set { if (opValue != value) { opValue = value; OnPropertyChanged(); } } }

        public bool HasRow1 => HasBP || HasSQ;
        public bool HasRow2 => HasDL || HasBR;
        public bool HasRow3 => HasOP;

        public string TotalMaxKg
        {
            get => totalMaxKg;
            set
            {
                if (totalMaxKg != value)
                {
                    totalMaxKg = value;
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

                // ✅ 1. Username yükle: Preferences → JWT → API sıralamasıyla
                Username = Preferences.Get("username", string.Empty);

                if (string.IsNullOrWhiteSpace(Username))
                {
                    // 1a. JWT token'dan oku
                    var token = await authService.GetTokenAsync();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        var handler = new JwtSecurityTokenHandler();
                        var jwt = handler.ReadJwtToken(token);
                        var uniqueName = jwt.Claims
                            .FirstOrDefault(c => c.Type == "unique_name")
                            ?.Value;

                        if (!string.IsNullOrWhiteSpace(uniqueName))
                        {
                            Username = uniqueName;
                            Preferences.Set("username", uniqueName);
                        }
                    }
                }

                // 1b. Hâlâ boşsa API'den çek (login sonrası Preferences/JWT henüz hazır değilse)
                if (string.IsNullOrWhiteSpace(Username))
                {
                    try
                    {
                        var profile = await authService.GetProfileAsync();
                        if (profile != null && !string.IsNullOrWhiteSpace(profile.Username))
                        {
                            Username = profile.Username;
                            Preferences.Set("username", profile.Username);
                        }
                    }
                    catch
                    {
                        // API erişilemiyorsa sessizce geç
                    }
                }

                if (string.IsNullOrWhiteSpace(Username))
                    Username = "Kullanıcı";

                // ✅ 2. Avatar fresh load et
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
                var workoutSummaries = await workoutService.GetWorkoutsAsync();

                // Compound hareketler için max ağırlık tablosu (kart sırasıyla)
                var maxWeights = new Dictionary<string, double>
                {
                    ["BP"] = 0,   // Bench Press
                    ["SQ"] = 0,   // Squat
                    ["DL"] = 0,   // Deadlift
                    ["BR"] = 0,   // Barbell Row
                    ["OP"] = 0    // Overhead Press
                };

                // Yalnızca en az 1 set içeren antrenman günleri sayılır.
                var activeDates = new HashSet<DateTime>();

                if (workoutSummaries != null && workoutSummaries.Count > 0)
                {
                    // ── ADIM 1: Tüm workout detaylarını PARALEL çek ──────────────────
                    // Eski: N sıralı await → her biri bir öncekini bekler
                    // Yeni: hepsi aynı anda, toplam süre en yavaş isteğin süresi kadar
                    var workoutDetailTasks = workoutSummaries
                        .Select(s => workoutService.GetWorkoutByIdAsync(s.Id));
                    var workoutDetails = await Task.WhenAll(workoutDetailTasks);

                    // ── ADIM 2: Tüm egzersiz ID'lerini ve tarihlerini topla ──────────
                    var exerciseRequests = workoutSummaries
                        .Zip(workoutDetails, (summary, detail) => (summary.WorkoutDate, detail))
                        .Where(p => p.detail?.Exercises != null)
                        .SelectMany(p => p.detail.Exercises
                            .Select(e => (WorkoutDate: p.WorkoutDate, ExerciseId: e.Id)))
                        .ToList();

                    // ── ADIM 3: Tüm egzersiz detaylarını PARALEL çek ─────────────────
                    var exerciseDetailTasks = exerciseRequests
                        .Select(r => exerciseService.GetExerciseByIdAsync(r.ExerciseId));
                    var exerciseDetails = await Task.WhenAll(exerciseDetailTasks);

                    // ── ADIM 4: Sonuçları işle ───────────────────────────────────────
                    for (int i = 0; i < exerciseDetails.Length; i++)
                    {
                        var exercise = exerciseDetails[i];
                        var workoutDate = exerciseRequests[i].WorkoutDate;

                        if (exercise?.ExerciseSets == null || exercise.ExerciseSets.Count == 0) continue;

                        activeDates.Add(workoutDate.Date);

                        var maxInSet = exercise.ExerciseSets.Max(s => s.WeightInKg);
                        var name = exercise.ExerciseName ?? string.Empty;

                        if (name.Contains("Bench Press", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Bench", StringComparison.OrdinalIgnoreCase))
                            maxWeights["BP"] = Math.Max(maxWeights["BP"], maxInSet);
                        else if (name.Contains("Squat", StringComparison.OrdinalIgnoreCase))
                            maxWeights["SQ"] = Math.Max(maxWeights["SQ"], maxInSet);
                        else if (name.Contains("Deadlift", StringComparison.OrdinalIgnoreCase))
                            maxWeights["DL"] = Math.Max(maxWeights["DL"], maxInSet);
                        else if (name.Contains("Barbell Row", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Rows", StringComparison.OrdinalIgnoreCase))
                            maxWeights["BR"] = Math.Max(maxWeights["BR"], maxInSet);
                        else if (name.Contains("Overhead Press", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("OHP", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Shoulder Press", StringComparison.OrdinalIgnoreCase))
                            maxWeights["OP"] = Math.Max(maxWeights["OP"], maxInSet);
                    }
                }

                // Sabit pozisyonlu property'leri güncelle
                HasBP = maxWeights["BP"] > 0; BPValue = maxWeights["BP"].ToString("0");
                HasSQ = maxWeights["SQ"] > 0; SQValue = maxWeights["SQ"].ToString("0");
                HasDL = maxWeights["DL"] > 0; DLValue = maxWeights["DL"].ToString("0");
                HasBR = maxWeights["BR"] > 0; BRValue = maxWeights["BR"].ToString("0");
                HasOP = maxWeights["OP"] > 0; OPValue = maxWeights["OP"].ToString("0");

                // Overall: en az 3 egzersiz varsa ortalama, en yakın 10'a yuvarla
                var activeValues = maxWeights.Values.Where(v => v > 0).ToList();
                if (activeValues.Count >= 3)
                {
                    var avg = activeValues.Average();
                    var rounded = Math.Round(avg / 10.0, MidpointRounding.AwayFromZero) * 10;
                    TotalMaxKg = rounded.ToString("0");
                }
                else
                {
                    TotalMaxKg = "0";
                }

                System.Diagnostics.Debug.WriteLine($"[MainPage] FUT Stats: {activeValues.Count} egzersiz, Overall={TotalMaxKg}, AktifGün={activeDates.Count}");

                // ── Streak ve bu haftaki antrenmanlar (yalnızca aktif günler) ──
                CalculateStreak(activeDates);
                CalculateWeeklyWorkouts(activeDates);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] LoadFutCardStatsAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// Bu haftanın Pazartesi–bugün arasındaki aktif günleri sayar.
        /// "Aktif gün" = en az 1 egzersiz seti olan gün.
        /// </summary>
        private void CalculateWeeklyWorkouts(HashSet<DateTime> activeDates)
        {
            if (activeDates.Count == 0) { WeeklyWorkouts = "0"; return; }

            var today = DateTime.Today;
            int daysFromMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
            var weekStart = today.AddDays(-daysFromMonday);

            var count = activeDates.Count(d => d >= weekStart && d <= today);
            WeeklyWorkouts = count.ToString();
            System.Diagnostics.Debug.WriteLine($"[MainPage] Bu hafta: {count} gün ({weekStart:dd.MM} – {today:dd.MM})");
        }

        /// <summary>
        /// Ardışık aktif günleri sayar. İki ardışık aktif gün arasındaki boşluk
        /// 7 günü geçerse zincir kırılır ve streak = 0 olur.
        /// "Aktif gün" = en az 1 egzersiz seti olan gün.
        /// </summary>
        private void CalculateStreak(HashSet<DateTime> activeDates)
        {
            if (activeDates.Count == 0) { Streak = 0; return; }

            var today = DateTime.Today;
            var sorted = activeDates.OrderByDescending(d => d).ToList();

            // Son aktif günden bu yana 7 günden fazla geçtiyse streak sıfır
            if ((today - sorted[0]).TotalDays > 7)
            {
                Streak = 0;
                return;
            }

            // Ardışık günleri say; iki ardışık tarih arası > 7 gün → zincir kırılır
            int count = 1;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                double gap = (sorted[i] - sorted[i + 1]).TotalDays;
                if (gap <= 7)
                    count++;
                else
                    break;
            }

            Streak = count;
        }
    }

    public class FutCardStat
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class CardStatRow
    {
        public FutCardStat? Left { get; set; }
        public FutCardStat? Right { get; set; }
        public bool HasRight => Right != null;
    }
}
