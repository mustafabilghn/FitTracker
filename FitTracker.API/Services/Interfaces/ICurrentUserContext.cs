namespace FitTrackr.API.Services.Interfaces
{
    /// <summary>
    /// İsteği yapan (kimliği doğrulanmış) kullanıcıyı request scope'unda taşır.
    /// Değer yalnızca sunucu tarafında, kimliği doğrulanmış userId'den set edilir; LLM/tool argümanlarından
    /// asla alınmaz. Bu sayede FitBot plugin'leri modelin seçtiği değil, her zaman isteği yapan kullanıcının
    /// verisini okur.
    /// </summary>
    public interface ICurrentUserContext
    {
        /// <summary>Henüz set edilmediyse null. Plugin'ler null durumunda veri okumaz (fail closed).</summary>
        string? UserId { get; }

        /// <summary>
        /// Kullanıcıyı set eder. Aynı scope içinde farklı bir kullanıcıyla ikinci kez çağrılırsa
        /// <see cref="System.InvalidOperationException"/> fırlatır.
        /// </summary>
        void SetUser(string userId);
    }
}
