using System.Collections.Concurrent;

namespace FitTrackr.API.Services
{
    /// <summary>
    /// Şifre sıfırlama kodlarını bellekte geçici olarak saklar.
    /// Her kayıt 15 dakika sonra geçersiz olur.
    /// </summary>
    public class PasswordResetService
    {
        private static readonly ConcurrentDictionary<string, ResetEntry> _store = new(StringComparer.OrdinalIgnoreCase);

        private const int ExpiryMinutes = 15;

        private sealed record ResetEntry(string Code, string IdentityToken, DateTime ExpiresAt);

        /// <summary>Yeni bir 6 haneli kod üretir ve saklar; eski kodları temizler.</summary>
        public string Store(string email, string identityToken)
        {
            Cleanup();

            var code = GenerateCode();
            var entry = new ResetEntry(code, identityToken, DateTime.UtcNow.AddMinutes(ExpiryMinutes));
            _store[email] = entry;
            return code;
        }

        /// <summary>Kod doğruysa Identity token'ı döner; hatalı veya süresi dolmuşsa null döner.</summary>
        public string? Validate(string email, string code)
        {
            if (!_store.TryGetValue(email, out var entry))
                return null;

            if (entry.ExpiresAt < DateTime.UtcNow)
            {
                _store.TryRemove(email, out _);
                return null;
            }

            if (!string.Equals(entry.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;

            _store.TryRemove(email, out _);
            return entry.IdentityToken;
        }

        private static string GenerateCode()
        {
            // 6 haneli sayısal kod (000000 – 999999)
            return Random.Shared.Next(0, 1_000_000).ToString("D6");
        }

        private static void Cleanup()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _store.Keys)
            {
                if (_store.TryGetValue(key, out var e) && e.ExpiresAt < now)
                    _store.TryRemove(key, out _);
            }
        }
    }
}
