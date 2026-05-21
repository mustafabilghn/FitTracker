using FitTrackr.API.Models.Domain;

namespace FitTrackr.API.Repositories
{
    public interface ITokenRepository
    {
        string CreateJWTToken(ApplicationUser user, List<string> roles);
    }
}
