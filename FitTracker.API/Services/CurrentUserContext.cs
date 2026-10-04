using System;
using FitTrackr.API.Services.Interfaces;

namespace FitTrackr.API.Services
{
    /// <summary>Request-scoped (DI: scoped) <see cref="ICurrentUserContext"/> implementasyonu.</summary>
    public sealed class CurrentUserContext : ICurrentUserContext
    {
        public string? UserId { get; private set; }

        public void SetUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User id must not be empty.", nameof(userId));

            // Aynı request scope'unda kullanıcı değiştirilemez: yanlışlıkla (ya da kötü niyetle) başka bir
            // kullanıcının verisine geçiş yapılmasını engeller.
            if (UserId is not null && !string.Equals(UserId, userId, StringComparison.Ordinal))
                throw new InvalidOperationException("The current user is already set for this request scope and cannot be changed.");

            UserId = userId;
        }
    }
}
