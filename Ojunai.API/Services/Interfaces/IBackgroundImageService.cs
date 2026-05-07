using Microsoft.AspNetCore.Http;

namespace Ojunai.API.Services.Interfaces;

public interface IBackgroundImageService
{
    /// <summary>
    /// Validates, decodes, resizes, strips metadata, re-encodes the user-supplied image,
    /// and saves it to the per-business uploads directory. Returns the new filename
    /// (UUID-based, JPEG). Throws InvalidOperationException with a user-safe message on
    /// any validation failure. Replaces any previous background atomically.
    /// </summary>
    Task<string> SaveAsync(Guid businessId, IFormFile file);

    /// <summary>Removes the current background image file (if any) and returns true if one was deleted.</summary>
    Task<bool> RemoveAsync(Guid businessId);
}
