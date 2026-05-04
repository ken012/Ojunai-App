namespace Ojunai.API.Services.Interfaces;

public interface IWhatsAppService
{
    Task HandleInboundAsync(string from, string messageId, string text);
    Task SendMessageAsync(string to, string text, Guid? businessId = null, Guid? userId = null);
}
