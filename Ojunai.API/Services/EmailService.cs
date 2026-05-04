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

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
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
}
