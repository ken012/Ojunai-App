using BizPilot.API.Common;

namespace BizPilot.API.Jobs;

public class TrialRevertJobService
{
    private readonly PlanGuard _planGuard;
    private readonly ILogger<TrialRevertJobService> _logger;

    public TrialRevertJobService(PlanGuard planGuard, ILogger<TrialRevertJobService> logger)
    {
        _planGuard = planGuard;
        _logger = logger;
    }

    public async Task RevertExpiredTrialsAsync()
    {
        try
        {
            await _planGuard.RevertExpiredTrialsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert expired trials");
        }
    }
}
