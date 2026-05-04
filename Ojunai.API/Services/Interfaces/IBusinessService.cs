using Ojunai.API.DTOs.Auth;
using Ojunai.API.DTOs.Business;

namespace Ojunai.API.Services.Interfaces;

public interface IBusinessService
{
    Task<BusinessDto> UpdateAsync(Guid businessId, UpdateBusinessRequest request);
    Task<BusinessDto> GetByIdAsync(Guid businessId);
}
