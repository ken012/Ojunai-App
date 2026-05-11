namespace Ojunai.API.Common;

public static class EntrySource
{
    public const string Manual = "Manual";
    public const string Dashboard = "Dashboard";
    public const string WhatsApp = "WhatsApp";
    public const string Telegram = "Telegram";
    public const string Messenger = "Messenger";
    public const string Voice = "Voice";
    public const string Import = "Import";

    /// <summary>Sources that represent AI-recorded actions (vs user-typed Manual/Dashboard or bulk Import).</summary>
    public static readonly string[] AiSources = { WhatsApp, Telegram, Messenger, Voice };
}
