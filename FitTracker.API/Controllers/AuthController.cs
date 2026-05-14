using FitTrackr.API.Data;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using Google.Apis.Auth;
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
        private readonly IEmailService emailService;
        private readonly PasswordResetService passwordResetService;

        public AuthController(
            UserManager<IdentityUser> userManager,
            ITokenRepository tokenRepository,
            IEmailService emailService,
            PasswordResetService passwordResetService)
        {
            this.userManager = userManager;
            this.tokenRepository = tokenRepository;
            this.emailService = emailService;
            this.passwordResetService = passwordResetService;
        }

        [HttpPost]
        [Route("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto registerRequestDto)
        {
            var existingEmail = await userManager.FindByEmailAsync(registerRequestDto.Email);
            if (existingEmail != null)
                return BadRequest("Bu e-posta adresi zaten kullanılıyor.");

            var existingUsername = await userManager.FindByNameAsync(registerRequestDto.Username);
            if (existingUsername != null)
                return BadRequest("Bu kullanıcı adı zaten kullanılıyor.");

            var user = new IdentityUser
            {
                UserName = registerRequestDto.Username,
                Email = registerRequestDto.Email
            };

            var result = await userManager.CreateAsync(user, registerRequestDto.Password);

            if (result.Succeeded)
            {
                if (registerRequestDto.Roles is not null && registerRequestDto.Roles.Any())
                {
                    result = await userManager.AddToRolesAsync(user, registerRequestDto.Roles);

                    if (result.Succeeded)
                        return Ok("Kullanıcı başarıyla oluşturuldu.");
                }
            }

            var errors = result.Errors.Select(e => e.Description);
            return BadRequest(string.Join(" ", errors));
        }

        [HttpPost]
        [Route("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto loginRequestDto)
        {
            // E-posta ile kullanıcı bul
            var user = await userManager.FindByEmailAsync(loginRequestDto.Email);

            // Geriye dönük uyumluluk: e-posta bulunamazsa kullanıcı adıyla dene
            // (e-posta alanında kullanıcı adı kaydedilen eski kullanıcılar için)
            if (user == null)
                user = await userManager.FindByNameAsync(loginRequestDto.Email);

            if (user is not null)
            {
                var checkPasswordResult = await userManager.CheckPasswordAsync(user, loginRequestDto.Password);

                if (checkPasswordResult)
                {
                    var roles = await userManager.GetRolesAsync(user);

                    if (roles is not null)
                    {
                        var jwtToken = tokenRepository.CreateJWTToken(user, roles.ToList());

                        return Ok(new LoginResponseDto { JwtToken = jwtToken });
                    }
                }
            }

            return BadRequest("E-posta veya şifre hatalı.");
        }

        [HttpDelete]
        [Route("delete-account")]
        [Authorize]
        public async Task<IActionResult> DeleteAccount([FromServices] FitTrackrDbContext fitTrackrDbContext)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
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

        /// <summary>
        /// Kullanıcı e-postasına 6 haneli şifre sıfırlama kodu gönderir.
        /// E-posta bulunamazsa güvenlik için yine 200 döner (kullanıcı keşfi engellemek için).
        /// </summary>
        [HttpPost]
        [Route("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest("E-posta adresi gerekli.");

            var user = await userManager.FindByEmailAsync(request.Email.Trim());

            // Kullanıcı bulunamasa bile 200 dön — e-posta adresi keşfini engelle
            if (user == null)
                return Ok();

            var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var code = passwordResetService.Store(request.Email.Trim(), identityToken);

            try
            {
                await emailService.SendPasswordResetCodeAsync(request.Email.Trim(), code);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"E-posta gönderilemedi: {ex.Message}");
            }

            return Ok();
        }

        /// <summary>
        /// 6 haneli kod + yeni şifre ile şifreyi sıfırlar.
        /// </summary>
        [HttpPost]
        [Route("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Code) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest("Tüm alanlar zorunludur.");

            var user = await userManager.FindByEmailAsync(request.Email.Trim());
            if (user == null)
                return BadRequest("Geçersiz kod veya e-posta adresi.");

            var identityToken = passwordResetService.Validate(request.Email.Trim(), request.Code.Trim());
            if (identityToken == null)
                return BadRequest("Kod hatalı veya süresi dolmuş.");

            var result = await userManager.ResetPasswordAsync(user, identityToken, request.NewPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join(" ", result.Errors.Select(e => e.Description));
                return BadRequest(errors);
            }

            return Ok();
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
                return BadRequest("Kullanıcı adı boş olamaz.");

            // Farklı bir kullanıcı bu kullanıcı adını kullanıyor mu kontrol et
            var existingUser = await userManager.FindByNameAsync(request.Username);
            if (existingUser != null && existingUser.Id != userId)
                return BadRequest("Bu kullanıcı adı zaten kullanılıyor.");

            // Sadece display name (UserName) güncellenir; e-posta (login kimliği) değişmez
            user.UserName = request.Username;

            var result = await userManager.UpdateAsync(user);

            if (result.Succeeded)
                return Ok();

            return BadRequest("Profil güncellenirken bir hata oluştu.");
        }

        /// <summary>
        /// Google, tarayıcı üzerinden bu endpoint'e authorization code gönderir.
        /// Backend kodu doğrudan işlemek yerine fittrackr:// deep link ile MAUI'ye iletir.
        /// Bu sayede Web OAuth client ile HTTPS redirect kullanılabilir.
        /// </summary>
        [HttpGet]
        [Route("google-callback")]
        public IActionResult GoogleCallback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                return Redirect(
                    $"fittrackr://auth?error={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
                return BadRequest("Authorization code eksik.");

            // Kodu ve state'i MAUI deep link'ine aktar — WebAuthenticator yakalar
            var deepLink = $"fittrackr://auth?code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrEmpty(state))
                deepLink += $"&state={Uri.EscapeDataString(state)}";

            return Redirect(deepLink);
        }

        /// <summary>
        /// Google OAuth PKCE akışından gelen authorization code'u backend'de token exchange ile doğrular.
        /// client_secret yalnızca backend'de tutulur; MAUI uygulaması kodu görmez.
        /// </summary>
        [HttpPost]
        [Route("google-login")]
        public async Task<IActionResult> GoogleLogin(
            [FromBody] GoogleLoginRequestDto request,
            [FromServices] IGoogleAuthService googleAuthService)
        {
            if (string.IsNullOrWhiteSpace(request.Code) ||
                string.IsNullOrWhiteSpace(request.CodeVerifier) ||
                string.IsNullOrWhiteSpace(request.RedirectUri))
                return BadRequest("Code, CodeVerifier ve RedirectUri zorunludur.");

            // 1. Kodu backend'de id_token ile değiştir ve Google key'leriyle doğrula
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await googleAuthService.ExchangeAndValidateCodeAsync(
                    request.Code,
                    request.CodeVerifier,
                    request.RedirectUri);
            }
            catch (InvalidJwtException)
            {
                return BadRequest("Geçersiz veya süresi dolmuş Google token.");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            var email = payload.Email;
            var googleUserId = payload.Subject; // Google'ın kalıcı kullanıcı kimliği
            var displayName = payload.Name ?? payload.GivenName ?? email.Split('@')[0];

            // 2. Bu Google hesabına bağlı mevcut kullanıcıyı ara
            var user = await userManager.FindByLoginAsync("Google", googleUserId);

            if (user == null)
            {
                // 3. Aynı e-posta ile normal hesap açılmış olabilir — var mı kontrol et
                user = await userManager.FindByEmailAsync(email);

                if (user == null)
                {
                    // 4. Tamamen yeni kullanıcı — oluştur
                    var username = await GenerateUniqueUsernameAsync(displayName);

                    user = new IdentityUser
                    {
                        UserName = username,
                        Email = email,
                        // Google zaten e-postayı doğrulamıştır
                        EmailConfirmed = true
                    };

                    var createResult = await userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        var errors = string.Join(" ", createResult.Errors.Select(e => e.Description));
                        return BadRequest(errors);
                    }

                    await userManager.AddToRolesAsync(user, new[] { "Reader", "Writer" });
                }

                // 5. Google login bilgisini hesaba bağla (mevcut veya yeni hesap)
                await userManager.AddLoginAsync(
                    user,
                    new UserLoginInfo("Google", googleUserId, "Google"));
            }

            // 6. Mevcut JWT altyapısını kullanarak token üret
            var roles = await userManager.GetRolesAsync(user);
            var jwtToken = tokenRepository.CreateJWTToken(user, roles.ToList());

            return Ok(new LoginResponseDto { JwtToken = jwtToken });
        }

        /// <summary>
        /// Google display name'inden benzersiz bir kullanıcı adı üretir.
        /// Çakışma durumunda sonuna sayı ekler: "JohnDoe", "JohnDoe1", "JohnDoe2" ...
        /// </summary>
        private async Task<string> GenerateUniqueUsernameAsync(string displayName)
        {
            // Sadece Identity'nin izin verdiği karakterleri tut
            var sanitized = new string(displayName
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
                .ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "user";

            var candidate = sanitized;
            var counter = 1;

            while (await userManager.FindByNameAsync(candidate) != null)
                candidate = $"{sanitized}{counter++}";

            return candidate;
        }
    }
}
