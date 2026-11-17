using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CareNexus.Services;

// Sends emails through Gmail (MailKit package).
// Needs in User Secrets:
//   "Email": { "From": "yourapp@gmail.com", "AppPassword": "16-letter Gmail app password" }
public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    // returns true if the email was sent, false if it failed
    public bool Send(string toEmail, string subject, string htmlBody)
    {
        try
        {
            string? fromEmail = _config["Email:From"];
            string? appPassword = _config["Email:AppPassword"];

            if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(appPassword))
            {
                _logger.LogWarning("Email is not set up in User Secrets (Email:From / Email:AppPassword).");
                return false;
            }

            // 1. write the email
            MimeMessage message = new MimeMessage();
            message.From.Add(new MailboxAddress(HospitalInfo.Name, fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            // 2. connect to Gmail, log in, send, disconnect
            using (SmtpClient smtp = new SmtpClient())
            {
                smtp.Connect("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                smtp.Authenticate(fromEmail, appPassword);
                smtp.Send(message);
                smtp.Disconnect(true);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email to {To} failed", toEmail);
            return false;
        }
    }
}
