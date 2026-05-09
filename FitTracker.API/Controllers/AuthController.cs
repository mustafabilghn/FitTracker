using FitTrackr.API.Data;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FitTrackr.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> userManager;
        private readonly ITokenRepository tokenRepository;

        public AuthController(UserManager<IdentityUser> userManager, ITokenRepository tokenRepository)
        {
            this.userManager = userManager;
            this.tokenRepository = tokenRepository;
        }

        [HttpPost]
        [Route("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto registerRequestDto)
        {
            var user = new IdentityUser
            {
                UserName = registerRequestDto.Username,
                Email = registerRequestDto.Username
            };

            var result = await userManager.CreateAsync(user, registerRequestDto.Password);

            if (result.Succeeded)
            {
                if (registerRequestDto.Roles is not null && registerRequestDto.Roles.Any())
                {
                    result = await userManager.AddToRolesAsync(user, registerRequestDto.Roles);

                    if (result.Succeeded)
                    {
                        return Ok("User was registered,please login");
                    }
                }
            }

            return BadRequest("Some error occurred while registering the user");
        }

        [HttpPost]
        [Route("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto loginRequestDto)
        {
            var user = await userManager.FindByEmailAsync(loginRequestDto.Username);

            if (user is not null)
            {
                var checkPasswordResult = await userManager.CheckPasswordAsync(user, loginRequestDto.Password);

                if (checkPasswordResult)
                {
                    var roles = await userManager.GetRolesAsync(user);

                    if (roles is not null)
                    {
                        var jwtToken = tokenRepository.CreateJWTToken(user, roles.ToList());

                        var response = new LoginResponseDto
                        {
                            JwtToken = jwtToken
                        };

                        return Ok(response);
                    }
                }
            }

            return BadRequest("Invalid login attempt");
        }

        [HttpDelete]
        [Route("delete-account")]
        [Authorize]
        public async Task<IActionResult> DeleteAccount([FromServices] FitTrackrDbContext fitTrackrDbContext)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if(userId == null)
                return Unauthorized();

            var user = await userManager.FindByIdAsync(userId);

            if (user == null)
                return NotFound();

            var workouts = await fitTrackrDbContext.Workouts
                .Include(w => w.Exercises)
                .ThenInclude(e => e.ExerciseSets)
                .Where(w => w.userId == userId)
                .ToListAsync();

            fitTrackrDbContext.RemoveRange(workouts);
            await fitTrackrDbContext.SaveChangesAsync();

            var result = await userManager.DeleteAsync(user);

            if (result.Succeeded)
                return Ok();

            return BadRequest("Hesap silinirken bir hata oluştu.");
        }

        [HttpGet]
        [Route("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
                return Unauthorized();

            var user = await userManager.FindByIdAsync(userId);

            if (user == null)
                return NotFound();

            // ✅ FİKS: IdentityUser'in profil property'leri yok, sadece Username dönüyoruz
            // TODO: Custom user entity oluştur ve HeightCm, WeightKg, Gender, Goal ekle
            var profile = new
            {
                username = user.UserName,
                heightCm = (int?)null,
                weightKg = (int?)null,
                gender = (string?)null,
                goal = (string?)null
            };

            return Ok(profile);
        }

        [HttpPut]
        [Route("profile")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
                return Unauthorized();

            var user = await userManager.FindByIdAsync(userId);

            if (user == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(request.Username))
                return BadRequest("Username cannot be empty");

            // ✅ FİKS: IdentityUser'in profil property'leri yok
            // TODO: Custom user entity oluştur ve şu property'leri ekle:
            // user.HeightCm = request.HeightCm;
            // user.WeightKg = request.WeightKg;
            // user.Gender = request.Gender;
            // user.Goal = request.Goal;

            // Şu an sadece username update edebiliriz
            user.UserName = request.Username;
            user.Email = request.Username;

            var result = await userManager.UpdateAsync(user);

            if (result.Succeeded)
                return Ok();

            return BadRequest("Profile update failed");
        }
    }
}
