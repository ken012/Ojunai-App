namespace Ojunai.API.Models.Messaging;

/// <summary>
/// One inbound or outbound media item — image, document (PDF), audio, video.
/// Stored URLs may be channel-provider hosted (Twilio media URL, Telegram file_id resolved
/// to a download URL) or our own (e.g. an Ojunai-hosted PDF receipt).
/// </summary>
public sealed record MediaAttachment(
    string MimeType,
    string Url,
    string? Caption = null,
    string? FileName = null);
