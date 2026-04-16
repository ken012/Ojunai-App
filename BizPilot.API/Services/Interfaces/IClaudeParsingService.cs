using BizPilot.API.DTOs.Parsing;

namespace BizPilot.API.Services.Interfaces;

public interface IClaudeParsingService
{
    Task<ParsedMessage> ParseAsync(string message, BusinessContext context, List<(string Role, string Content)>? history = null);
}
