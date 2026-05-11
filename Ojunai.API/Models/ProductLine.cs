namespace Ojunai.API.Models;

/// <summary>
/// Identifies which product line a Subscription / ActionUsage / BusinessOverride row belongs to.
///
/// Three independent product lines:
///   - <c>Dashboard</c> — the web dashboard SaaS (tiers: starter, lite, operator, professional, scale, enterprise)
///   - <c>Wbos</c>      — WhatsApp Business OS, a chat-native parallel product (tiers: solo, pro, scale)
///   - <c>VoiceAi</c>   — Voice AI standalone product (separate from the dashboard add-on variant)
///
/// A single Business can hold one active Subscription per product line — they bill, cap, and gate independently.
/// Stored as a string column in Postgres so adding a new product line later is a config change, not a schema migration.
/// </summary>
public enum ProductLine
{
    Dashboard = 1,
    Wbos = 2,
    VoiceAi = 3,
}
