using Ojunai.API.DTOs.Parsing;

namespace Ojunai.API.Services.Interfaces;

public interface IClaudeParsingService
{
    Task<ParsedMessage> ParseAsync(string message, BusinessContext context, List<(string Role, string Content)>? history = null);
}
