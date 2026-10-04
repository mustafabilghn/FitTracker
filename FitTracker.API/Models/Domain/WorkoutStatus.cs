namespace FitTrackr.API.Models.Domain
{
    /// <summary>
    /// Bir <see cref="Workout"/>'un yaşam döngüsü durumu.
    ///
    /// Completed = 0 bilinçli bir seçimdir: veritabanında int olarak saklanır ve migration mevcut tüm kayıtları
    /// varsayılan değer (0) ile Completed yapar; mevcut kayıtların anlamı "kullanıcının yaptığı antrenman"dır.
    /// Geçiş yalnızca Planned → Completed yönündedir.
    /// </summary>
    public enum WorkoutStatus
    {
        /// <summary>Kullanıcının gerçekten yaptığı antrenman. Analiz, trend, baseline ve dashboard yalnızca bunları kullanır.</summary>
        Completed = 0,

        /// <summary>Henüz yapılmamış plan (ör. FitBot SaveWorkoutPlan). Geçmiş antrenman verisi olarak kullanılmaz.</summary>
        Planned = 1
    }
}
