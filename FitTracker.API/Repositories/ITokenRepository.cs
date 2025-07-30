using Microsoft.AspNetCore.Identity;

namespace FitTrackr.API.Repositories
{
    public interface ITokenRepository
    {
        string CreateJWTToken(IdentityUser user, List<string> roles);
    }
}
