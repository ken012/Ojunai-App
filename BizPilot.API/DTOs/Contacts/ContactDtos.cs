using System.ComponentModel.DataAnnotations;
using BizPilot.API.Models;

namespace BizPilot.API.DTOs.Contacts;

public class CreateContactRequest
{
    [Required, MinLength(1), MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(20)] public string? PhoneNumber { get; set; }
    public ContactType Type { get; set; } = ContactType.Customer;
}

public class ContactDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal OutstandingReceivable { get; set; }
    public decimal OutstandingPayable { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
