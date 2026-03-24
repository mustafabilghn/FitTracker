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

            var workouts = fitTrackrDbContext.Workouts
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
    }
}
