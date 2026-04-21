using System.Security.Cryptography;
using BizPilot.API.Data;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Common;

public static class AccountNumberGenerator
{
    public static async Task<string> GenerateUniqueAsync(AppDbContext db)
    {
        const int maxAttempts = 10;
        for (var i = 0; i < maxAttempts; i++)
        {
            var number = RandomNumberGenerator.GetInt32(1000000, 10000000).ToString();
            if (!await db.Businesses.AnyAsync(b => b.AccountNumber == number))
                return number;
        }
        throw new InvalidOperationException("Failed to generate unique account number after maximum attempts.");
    }
}
