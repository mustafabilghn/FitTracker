using FitTrackr.API.Services.Interfaces;
using System.Net;
using System.Net.Mail;

namespace FitTrackr.API.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public SmtpEmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendPasswordResetCodeAsync(string toEmail, string code)
        {
            var host     = _config["Email:SmtpHost"]     ?? throw new InvalidOperationException("Email:SmtpHost yapılandırılmamış.");
            var port     = int.Parse(_config["Email:SmtpPort"] ?? "587");
            var user     = _config["Email:Username"]     ?? throw new InvalidOperationException("Email:Username yapılandırılmamış.");
            var password = _config["Email:Password"]     ?? throw new InvalidOperationException("Email:Password yapılandırılmamış.");
            var from     = _config["Email:FromAddress"]  ?? user;

            var subject = "FitTracker – Şifre Sıfırlama Kodu";
            var body = $"""
                Merhaba,

                Şifre sıfırlama talebinde bulundunuz.
                Aşağıdaki 6 haneli kodu uygulama içindeki "Şifremi Unuttum" ekranına girin:

                    {code}

                Bu kod 15 dakika geçerlidir.

                Eğer bu talebi siz yapmadıysanız bu e-postayı görmezden gelebilirsiniz.

                — FitTracker
                """;

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(user, password),
                EnableSsl = true
            };

            var message = new MailMessage(from, toEmail, subject, body)
            {
                IsBodyHtml = false
            };

            await client.SendMailAsync(message);
        }
    }
}
