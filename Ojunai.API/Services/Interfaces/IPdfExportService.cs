namespace Ojunai.API.Services.Interfaces;

public interface IPdfExportService
{
    Task<byte[]> GenerateReportPdfAsync(Guid businessId, string reportType, DateOnly from, DateOnly to);
}
