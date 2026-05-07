namespace Ojunai.API.Common;

public static class EntrySource
{
    public const string Manual = "Manual";
    public const string WhatsApp = "WhatsApp";
    public const string Voice = "Voice";
    public const string Import = "Import";

    /// <summary>Sources that represent AI-recorded actions (vs user-typed Manual or bulk Import).</summary>
    public static readonly string[] AiSources = { WhatsApp, Voice };
}
