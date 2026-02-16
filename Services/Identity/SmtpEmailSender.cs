using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Identity
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly EmailSmtpOptions _options;

        public SmtpEmailSender(IOptions<EmailSmtpOptions> options)
        {
            _options = options.Value;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrWhiteSpace(_options.Host))
            {
                throw new InvalidOperationException("Email SMTP is not configured. Set Email:Smtp settings.");
            }

            if (string.IsNullOrWhiteSpace(_options.UserName) || string.IsNullOrWhiteSpace(_options.Password))
            {
                throw new InvalidOperationException("Email SMTP credentials are not configured. Set Email:Smtp:UserName and Email:Smtp:Password.");
            }

            if (string.IsNullOrWhiteSpace(_options.FromEmail))
            {
                throw new InvalidOperationException("Email SMTP FromEmail is not configured. Set Email:Smtp:FromEmail.");
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromEmail, string.IsNullOrWhiteSpace(_options.FromName) ? _options.FromEmail : _options.FromName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };

            message.To.Add(new MailAddress(email));

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_options.UserName, _options.Password)
            };

            await client.SendMailAsync(message);
        }
    }
}
