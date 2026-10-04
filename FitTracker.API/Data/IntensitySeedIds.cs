using System;

namespace FitTrackr.API.Data
{
    /// <summary>
    /// Seed edilmiş Intensity referans kayıtlarının kalıcı (stable) ID'leri — TEK kaynak. Hem <see cref="FitTrackrDbContext"/>
    /// seed'i (HasData) hem de uygulama kodu bu sabitleri kullanır.
    ///
    /// Kod Level metnine değil ID'ye bağlanmalıdır: Level değerleri migration ile yerelleştirildi (UpdateSeeds:
    /// Low/Medium/High → Düşük/Orta/Yüksek), ID'ler ise model seed'inde ve migration geçmişinde aynıdır.
    /// </summary>
    public static class IntensitySeedIds
    {
        public static readonly Guid Low = new("04faaf32-4a41-4b4e-888f-9651092caa08");
        public static readonly Guid Medium = new("153480fc-718b-4610-bd4f-ead66fb24a3d");
        public static readonly Guid High = new("7d4ae440-c208-4b50-b252-88730b550d25");
    }
}
