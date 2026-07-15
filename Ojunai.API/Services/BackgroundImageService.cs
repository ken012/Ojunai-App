using Ojunai.API.Data;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Ojunai.API.Services;

/// <summary>
/// Handles upload + validation + sanitization of business background images.
///
/// Security pipeline (every layer is independently necessary):
///   1. Empty/oversized files rejected at controller level (RequestSizeLimit).
///   2. MIME whitelist (jpeg/png/webp only — SVG explicitly forbidden because of XSS risk).
///   3. Magic-byte check on first 12 bytes — extension and Content-Type are spoofable.
///   4. Image.Identify preflight — reads dimensions WITHOUT decoding pixels. Reject if
///      width × height > 50 megapixels. This defeats pixel-flood DoS where a tiny file
///      decompresses to gigabytes of RAM.
///   5. Full decode via ImageSharp — if the bytes don't actually decode as a valid image,
///      reject. Catches polyglot files (a JPEG that's also valid JS).
///   6. Resize to fit within 1920×1080.
///   7. Strip ALL metadata (EXIF / GPS / ICC / XMP / IPTC).
///   8. Re-encode as JPEG q85. We never serve user bytes — re-encoded output is clean.
///   9. UUID filename, scoped under {businessId}/. BusinessId comes from the JWT claim
///      (not request body), so there's no path-traversal route.
///  10. Old file is deleted on replace; same on explicit removal.
/// </summary>
public class BackgroundImageService : IBackgroundImageService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<BackgroundImageService> _logger;
    private readonly IActivityLogger _activity;

    private const long MaxFileSizeBytes = 5L * 1024 * 1024; // 5MB
    private const int MaxOutputWidth = 1920;
    private const int MaxOutputHeight = 1080;
    private const long MaxPixels = 50L * 1000 * 1000; // 50 megapixels — reject before decode
    private const int JpegQuality = 85;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp",
    };

    public BackgroundImageService(
        AppDbContext db,
        IConfiguration config,
        ILogger<BackgroundImageService> logger,
        IActivityLogger activity)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _activity = activity;
    }

    public async Task<string> SaveAsync(Guid businessId, IFormFile file)
    {
        // ── Layer 1: basic upload sanity ──────────────────────────────────────
        if (file == null || file.Length == 0)
            throw new InvalidOperationException("No file uploaded.");
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidOperationException($"File too large. Maximum size is {MaxFileSizeBytes / (1024 * 1024)}MB.");

        // ── Layer 2: MIME whitelist (still a defense, not the only one) ──────
        if (!AllowedContentTypes.Contains(file.ContentType ?? ""))
            throw new InvalidOperationException("Only JPEG, PNG, or WebP images are allowed.");

        // Read into memory once. Stream re-reads aren't supported on IFormFile
        // and the size cap above bounds memory.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        // ── Layer 3: magic-byte check ────────────────────────────────────────
        if (!IsAllowedImageSignature(bytes))
            throw new InvalidOperationException("This file isn't a valid JPEG, PNG, or WebP image.");

        // Belt + suspenders: explicitly reject SVG even if MIME/magic somehow let it through.
        // SVG starts with '<' or whitespace then '<' — completely incompatible with our whitelist
        // signatures, but a paranoid extra check is cheap.
        if (bytes.Length > 0 && (bytes[0] == 0x3C || (bytes.Length > 5 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF && bytes[3] == 0x3C)))
            throw new InvalidOperationException("SVG files are not supported.");

        // ── Layer 4: dimension preflight (no decode) ─────────────────────────
        ImageInfo info;
        try
        {
            using var preflight = new MemoryStream(bytes, writable: false);
            info = await Image.IdentifyAsync(preflight);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Couldn't read this image. Please upload a different file.");
        }
        if ((long)info.Width * info.Height > MaxPixels)
            throw new InvalidOperationException("Image dimensions are too large. Please use an image under 50 megapixels.");

        // ── Layer 5 + 6 + 7 + 8: decode → strip → resize → re-encode ─────────
        string newFilename;
        try
        {
            using var source = new MemoryStream(bytes, writable: false);
            using var image = await Image.LoadAsync(source);

            // Strip ALL metadata. ImageSharp keeps EXIF / ICC / XMP by default.
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max, // preserve aspect ratio, fit within bounds
                Size = new Size(MaxOutputWidth, MaxOutputHeight),
                Sampler = KnownResamplers.Lanczos3,
            }));

            newFilename = $"{Guid.NewGuid():N}.jpg";
            var (dir, path) = ResolvePaths(businessId, newFilename);
            Directory.CreateDirectory(dir);

            // ── Layer 9: write under businessId-scoped path with UUID name ───
            await using (var output = File.Create(path))
            {
                await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = JpegQuality });
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background image processing failed for business {BusinessId}", businessId);
            throw new InvalidOperationException("Couldn't process this image. Please try a different file.");
        }

        // ── Layer 10: replace previous, persist column ───────────────────────
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new InvalidOperationException("Business not found.");

        var oldFilename = business.BackgroundImageFileName;
        business.BackgroundImageFileName = newFilename;
        await _activity.LogAsync(businessId, "settings.branding_updated", "Business", businessId, business.Name, "uploaded a dashboard background image");
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(oldFilename))
            TryDeleteFile(businessId, oldFilename);

        _logger.LogInformation("Background image saved for business {BusinessId}: {Filename}", businessId, newFilename);
        return newFilename;
    }

    public async Task<bool> RemoveAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new InvalidOperationException("Business not found.");

        if (string.IsNullOrEmpty(business.BackgroundImageFileName)) return false;

        var filename = business.BackgroundImageFileName;
        business.BackgroundImageFileName = null;
        await _activity.LogAsync(businessId, "settings.branding_removed", "Business", businessId, business.Name, "removed the dashboard background image");
        await _db.SaveChangesAsync();

        TryDeleteFile(businessId, filename);
        _logger.LogInformation("Background image removed for business {BusinessId}", businessId);
        return true;
    }

    /// <summary>
    /// Magic-byte sniff. Layer 2's MIME check is from the upload form (spoofable);
    /// this checks the actual file content. Order matters: WebP's RIFF header is more
    /// distinctive than JPEG's, so testing either-or is fine, but be explicit.
    /// </summary>
    private static bool IsAllowedImageSignature(byte[] bytes)
    {
        if (bytes.Length < 12) return false;

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return true;

        // WebP: 'RIFF' .... 'WEBP'
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true;

        return false;
    }

    private (string Dir, string Path) ResolvePaths(Guid businessId, string filename)
    {
        var root = _config["Uploads:Root"] ?? "/var/www/ojunai-uploads";
        var dir = Path.Combine(root, "businesses", businessId.ToString("N"));
        return (dir, Path.Combine(dir, filename));
    }

    private void TryDeleteFile(Guid businessId, string filename)
    {
        try
        {
            var (_, path) = ResolvePaths(businessId, filename);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            // Don't fail the user-facing operation just because a stale file couldn't be cleaned.
            _logger.LogWarning(ex, "Couldn't delete old background file {File} for business {BusinessId}", filename, businessId);
        }
    }
}
