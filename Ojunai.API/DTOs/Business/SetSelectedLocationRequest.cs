namespace Ojunai.API.DTOs.Business;

/// <summary>Body for PUT /api/business/selected-location — the branch the current user is switching to.
/// <c>LocationId = null</c> means "All branches" (business-wide), allowed only for all-access roles.</summary>
public record SetSelectedLocationRequest(Guid? LocationId);
