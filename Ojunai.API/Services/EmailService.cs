using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Ojunai.API.Services;

public interface IEmailService
{
    /// <summary>
    /// Send an email with optional file attachments. Throws if SMTP is not configured
    /// or if sending fails.
    /// </summary>
    Task SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? plainBody = null,
        IEnumerable<EmailAttachment>? attachments = null);

    /// <summary>True if SMTP credentials are configured (otherwise the endpoint should 503).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Best-effort security notification email. Used for out-of-band alerts on sensitive
    /// account changes (password changed, phone changed, recovery used, etc.) so a
    /// compromised-account victim hears about it through a channel the attacker doesn't
    /// control. Never throws — if email is unconfigured, undeliverable, or the recipient
    /// has no email, the call quietly no-ops and logs.
    /// </summary>
    Task TrySendSecurityNotificationAsync(
        string? toAddress,
        string? toName,
        string action,
        string detail);
}

public record EmailAttachment(string Filename, byte[] Content, string ContentType);

/// <summary>
/// Provider-agnostic SMTP email sender. Reads config from "Email" section.
/// Works with SendGrid, Mailgun, Postmark, AWS SES, Workspace, anywhere with SMTP.
/// Required env / config:
///   Email__SmtpHost      e.g. smtp.sendgrid.net
///   Email__SmtpPort      587 (STARTTLS) or 465 (SSL)
///   Email__SmtpUsername
///   Email__SmtpPassword
///   Email__FromAddress   noreply@ojunai.com (must be verified by provider)
///   Email__FromName      Ojunai
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly ISuppressionService _suppression;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger, ISuppressionService suppression)
    {
        _config = config;
        _logger = logger;
        _suppression = suppression;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Email:SmtpHost"]) &&
        !string.IsNullOrWhiteSpace(_config["Email:FromAddress"]);

    public async Task SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        string? plainBody = null,
        IEnumerable<EmailAttachment>? attachments = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Email service is not configured. Set Email__SmtpHost, Email__SmtpUsername, Email__SmtpPassword, Email__FromAddress in environment.");

        // Reputation guard: refuse to send to any address SES has previously reported as
        // a hard bounce or complaint. Sending anyway would tank our sender reputation and
        // can get production access revoked.
        if (await _suppression.IsSuppressedAsync(toAddress))
        {
            _logger.LogWarning("Skipping send to suppressed address {To} subject={Subject}", toAddress, subject);
            return;
        }

        var host = _config["Email:SmtpHost"]!;
        var port = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var username = _config["Email:SmtpUsername"];
        var password = _config["Email:SmtpPassword"];
        var fromAddress = _config["Email:FromAddress"]!;
        var fromName = _config["Email:FromName"] ?? "Ojunai";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(new MailboxAddress(toName ?? toAddress, toAddress));
        message.Subject = subject;

        var builder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = plainBody ?? StripHtml(htmlBody),
        };

        if (attachments != null)
        {
            foreach (var att in attachments)
            {
                builder.Attachments.Add(att.Filename, att.Content, MimeKit.ContentType.Parse(att.ContentType));
            }
        }

        message.Body = builder.ToMessageBody();

        // STARTTLS for 587, implicit TLS for 465
        var secureOption = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(host, port, secureOption);
            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Email sent to {To} subject={Subject}", toAddress, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", toAddress);
            throw;
        }
    }

    private static string StripHtml(string html)
    {
        // Lightweight fallback for plain-text version. Not a full HTML→text conversion.
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }

    public async Task TrySendSecurityNotificationAsync(string? toAddress, string? toName, string action, string detail)
    {
        if (!IsConfigured) return;
        if (string.IsNullOrWhiteSpace(toAddress)) return;

        var when = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var safeAction = System.Net.WebUtility.HtmlEncode(action);
        var safeDetail = System.Net.WebUtility.HtmlEncode(detail);
        var safeName = System.Net.WebUtility.HtmlEncode(toName ?? "");

        var html = $@"<!doctype html><html><body style=""font-family: -apple-system, BlinkMacSystemFont, sans-serif; color: #0F172A; max-width: 560px; margin: 0 auto; padding: 24px;"">
  <h2 style=""color: #0F172A;"">Security alert on your Ojunai account</h2>
  <p>Hi {safeName},</p>
  <p>The following change was just made to your account:</p>
  <p style=""background:#F8FAFC; border-left: 3px solid #06B6D4; padding: 12px 16px; margin: 16px 0;"">
    <strong>{safeAction}</strong><br/>
    <span style=""color:#475569;"">{safeDetail}</span><br/>
    <span style=""color:#94A3B8; font-size:12px;"">{when}</span>
  </p>
  <p style=""color:#DC2626; font-size: 13px;"">If this wasn't you, contact support immediately. Your account may be compromised.</p>
  <p style=""color: #64748B; font-size: 12px; margin-top: 32px;"">You're receiving this because Ojunai sends a notification on every sensitive account change. We can't disable these — they're your safety net.</p>
</body></html>";
        var plain = $"Security alert on your Ojunai account\n\n{action}\n{detail}\n{when}\n\nIf this wasn't you, contact support immediately.";

        try
        {
            await SendAsync(toAddress, toName ?? "", $"Ojunai security alert: {action}", html, plain);
        }
        catch (Exception ex)
        {
            // Don't surface — caller's primary action already succeeded. We don't want
            // a flaky SMTP host to roll back a password change.
            _logger.LogWarning(ex, "Security notification email send failed to {To}", toAddress);
        }
    }
}
