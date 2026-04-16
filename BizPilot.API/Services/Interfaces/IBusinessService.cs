using BizPilot.API.DTOs.Auth;
using BizPilot.API.DTOs.Business;

namespace BizPilot.API.Services.Interfaces;

public interface IBusinessService
{
    Task<BusinessDto> UpdateAsync(Guid businessId, UpdateBusinessRequest request);
    Task<BusinessDto> GetByIdAsync(Guid businessId);
}
