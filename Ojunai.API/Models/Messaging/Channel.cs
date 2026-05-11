namespace Ojunai.API.Models.Messaging;

/// <summary>
/// Identifies which messaging channel a <see cref="ConversationMessage"/> arrived on
/// or should be sent through. Stored as an int in Postgres so the catalog can grow
/// without a column-type migration.
/// </summary>
public enum Channel
{
    Whatsapp = 1,
    Telegram = 2,
    Messenger = 3,
    Sms = 4,
}
