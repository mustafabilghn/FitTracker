using FitTrackr.MAUI.Services;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Pages;
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
        private int streakRecord = 0;
        private string weeklyWorkouts = "0";
        private int weeklyWorkoutsCount = 0;

        public int Streak
        {
            get => streak;
            set { if (streak != value) { streak = value; OnPropertyChanged(); } }
        }

        public int StreakRecord
        {
            get => streakRecord;
            set { if (streakRecord != value) { streakRecord = value; OnPropertyChanged(); } }
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

        public MainPage(WorkoutService workoutService, AuthService authService)
        {
            InitializeComponent();
            BindingContext = this;
            this.authService = authService;
            this.workoutService = workoutService;
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
                var hasPreloaded = workoutService.PreloadedDashboard != null;
                LoadingIndicator.IsVisible = !hasPreloaded;
                LoadingIndicator.IsRunning = !hasPreloaded;

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
        /// FUT CARD STATS: Tek bir dashboard endpoint'i ile tüm home screen verisini çeker.
        /// Eski implementasyon: 1 + N + N×M API çağrısı (paralel olsa bile çok fazla round-trip).
        /// Yeni implementasyon: 1 API çağrısı — backend hesaplayıp döndürüyor.
        /// </summary>
        private async Task LoadFutCardStatsAsync()
        {
            try
            {
                // Splash screen'de ön yüklendiyse cache'i kullan, sonra temizle
                var dashboard = workoutService.PreloadedDashboard
                    ?? await workoutService.GetDashboardAsync();
                workoutService.InvalidateDashboardCache();

                // Compound PR'ları güncelle
                HasBP = dashboard.BenchPressMaxKg > 0; BPValue = dashboard.BenchPressMaxKg.ToString("0");
                HasSQ = dashboard.SquatMaxKg > 0;      SQValue = dashboard.SquatMaxKg.ToString("0");
                HasDL = dashboard.DeadliftMaxKg > 0;   DLValue = dashboard.DeadliftMaxKg.ToString("0");
                HasBR = dashboard.BarbellRowMaxKg > 0; BRValue = dashboard.BarbellRowMaxKg.ToString("0");
                HasOP = dashboard.OhpMaxKg > 0;        OPValue = dashboard.OhpMaxKg.ToString("0");

                // Overall rating
                var activeValues = new[] {
                    dashboard.BenchPressMaxKg, dashboard.SquatMaxKg,
                    dashboard.DeadliftMaxKg, dashboard.BarbellRowMaxKg, dashboard.OhpMaxKg
                }.Where(v => v > 0).ToList();

                TotalMaxKg = activeValues.Count >= 3
                    ? Math.Round(activeValues.Average(), MidpointRounding.AwayFromZero).ToString("0")
                    : "0";

                // Streak ve haftalık antrenman sayısı
                var activeDates = new HashSet<DateTime>(
                    dashboard.ActiveDates.Select(d => d.Date));

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
            var weekStart = GetWeekMonday(today);

            var count = activeDates.Count(d => d >= weekStart && d <= today);
            weeklyWorkoutsCount = count;
            WeeklyWorkouts = count.ToString();
            System.Diagnostics.Debug.WriteLine($"[MainPage] Bu hafta: {count} gün ({weekStart:dd.MM} – {today:dd.MM})");
        }

        /// <summary>
        /// Haftalık seriyi hesaplar (Pazartesi–Pazar haftası bazlı).
        /// O hafta içinde en az 1 egzersiz seti varsa o hafta "aktif" sayılır.
        /// Tamamlanmış bir hafta boyunca hiçbir kayıt yoksa zincir kırılır.
        /// Cari hafta henüz bitmediği için, bu hafta kayıt yoksa geçen haftaya bakılır;
        /// geçen hafta da boşsa streak = 0.
        /// </summary>
        private void CalculateStreak(HashSet<DateTime> activeDates)
        {
            if (activeDates.Count == 0) { Streak = 0; return; }

            // Her aktif tarihi o haftanın Pazartesisi ile eşle
            var activeWeeks = new HashSet<DateTime>(
                activeDates.Select(d => GetWeekMonday(d))
            );

            var thisWeekMonday = GetWeekMonday(DateTime.Today);
            var lastWeekMonday = thisWeekMonday.AddDays(-7);

            // Başlangıç haftasını belirle:
            // Bu hafta aktifse buradan başla; yoksa geçen haftaya bak.
            // Geçen hafta da aktif değilse streak = 0 (tamamlanmış bir hafta kaçırıldı).
            DateTime startWeek;
            if (activeWeeks.Contains(thisWeekMonday))
                startWeek = thisWeekMonday;
            else if (activeWeeks.Contains(lastWeekMonday))
                startWeek = lastWeekMonday;
            else
            {
                Streak = 0;
                return;
            }

            // startWeek'ten geriye doğru ardışık aktif haftaları say
            int count = 0;
            var checkWeek = startWeek;
            while (activeWeeks.Contains(checkWeek))
            {
                count++;
                checkWeek = checkWeek.AddDays(-7);
            }

            Streak = count;

            // Rekor: tüm geçmiş haftalara bakarak en uzun ardışık seriyi hesapla
            StreakRecord = CalculateMaxStreak(activeWeeks);

            System.Diagnostics.Debug.WriteLine($"[MainPage] Haftalık seri: {count}, Rekor: {StreakRecord} (başlangıç: {startWeek:dd.MM.yyyy})");
        }

        /// <summary>
        /// Tüm aktif haftalar içindeki en uzun ardışık hafta serisini döndürür.
        /// Her hafta kendi geçmişiyle dinamik olarak hesaplanır — Preferences'a bağımlılık yok.
        /// </summary>
        private static int CalculateMaxStreak(HashSet<DateTime> activeWeeks)
        {
            if (activeWeeks.Count == 0) return 0;

            var sorted = activeWeeks.OrderBy(w => w).ToList();
            int maxStreak = 1;
            int current = 1;

            for (int i = 1; i < sorted.Count; i++)
            {
                if ((sorted[i] - sorted[i - 1]).TotalDays == 7)
                {
                    current++;
                    if (current > maxStreak) maxStreak = current;
                }
                else
                {
                    current = 1;
                }
            }

            return maxStreak;
        }

        /// <summary>
        /// Verilen tarihin bulunduğu haftanın Pazartesisini döndürür.
        /// </summary>
        private static DateTime GetWeekMonday(DateTime date)
        {
            int daysFromMonday = ((int)date.DayOfWeek - 1 + 7) % 7;
            return date.AddDays(-daysFromMonday).Date;
        }

        private async void OnStreakCardTapped(object sender, EventArgs e)
        {
            var page = new StreakDetailPage(Streak, StreakRecord);
            await Navigation.PushModalAsync(page, animated: true);
        }

        private async void OnWeeklyCardTapped(object sender, EventArgs e)
        {
            var page = new WeeklyDetailPage(weeklyWorkoutsCount);
            await Navigation.PushModalAsync(page, animated: true);
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
