using System.Text;

namespace BizPilot.API.Common;

public static class CsvParser
{
    public static List<Dictionary<string, string>> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();

        // Strip BOM
        if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1);

        // Normalize line endings
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return new List<Dictionary<string, string>>();

        // Auto-detect delimiter (comma vs semicolon)
        var headerLine = lines[0];
        var delimiter = headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';

        // Parse headers (case-insensitive, trimmed, aliased to canonical names)
        var headers = ParseRow(headerLine, delimiter)
            .Select(h => NormalizeHeader(h.Trim()))
            .ToArray();

        if (headers.Length > 50) return new List<Dictionary<string, string>>();

        var results = new List<Dictionary<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var values = ParseRow(line, delimiter);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < headers.Length && j < values.Length; j++)
            {
                var val = values[j].Trim();
                if (!string.IsNullOrEmpty(val)) row[headers[j]] = val;
            }
            if (row.Count > 0) results.Add(row);
        }

        return results;
    }

    private static string[] ParseRow(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // Skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // ProductName
        ["product name"] = "productname",
        ["productname"] = "productname",
        ["product"] = "productname",
        ["name"] = "productname",
        ["item"] = "productname",
        ["item name"] = "productname",

        // Quantity
        ["quantity"] = "quantity",
        ["qty"] = "quantity",
        ["stock"] = "quantity",
        ["amount"] = "amount",  // for expenses

        // Unit
        ["unit"] = "unit",
        ["units"] = "unit",
        ["uom"] = "unit",

        // CostPrice
        ["costprice"] = "costprice",
        ["cost price"] = "costprice",
        ["cost"] = "costprice",
        ["buying price"] = "costprice",
        ["purchase price"] = "costprice",
        ["cost price (₦)"] = "costprice",
        ["cost (₦)"] = "costprice",

        // SellingPrice
        ["sellingprice"] = "sellingprice",
        ["selling price"] = "sellingprice",
        ["sell price"] = "sellingprice",
        ["price"] = "sellingprice",
        ["selling price (₦)"] = "sellingprice",
        ["price (₦)"] = "sellingprice",

        // UnitPrice (for sales)
        ["unitprice"] = "unitprice",
        ["unit price"] = "unitprice",
        ["unit price (₦)"] = "unitprice",
        ["sale price"] = "unitprice",

        // Category
        ["category"] = "category",
        ["subcategory"] = "subcategory",
        ["sub category"] = "subcategory",

        // Threshold
        ["threshold"] = "threshold",
        ["low stock threshold"] = "threshold",
        ["min stock"] = "threshold",
        ["reorder level"] = "threshold",

        // Customer
        ["customername"] = "customername",
        ["customer name"] = "customername",
        ["customer"] = "customername",
        ["buyer"] = "customername",

        // Date (required for all import types)
        ["date"] = "date",
        ["saledate"] = "saledate",
        ["sale date"] = "saledate",
        ["expensedate"] = "expensedate",
        ["expense date"] = "expensedate",
        ["stockdate"] = "stockdate",
        ["stock date"] = "stockdate",
        ["debtdate"] = "debtdate",
        ["debt date"] = "debtdate",
        ["transaction date"] = "date",
        ["created"] = "date",
        ["created date"] = "date",

        // PaymentStatus
        ["paymentstatus"] = "paymentstatus",
        ["payment status"] = "paymentstatus",
        ["payment"] = "paymentstatus",
        ["status"] = "paymentstatus",

        // Expense fields
        ["paidto"] = "paidto",
        ["paid to"] = "paidto",
        ["vendor"] = "paidto",
        ["supplier"] = "paidto",
        ["notes"] = "notes",
        ["note"] = "notes",
        ["description"] = "notes",

        // Contact fields
        ["contactname"] = "contactname",
        ["contact name"] = "contactname",
        ["contact"] = "contactname",

        // Contact type
        ["contacttype"] = "contacttype",
        ["contact type"] = "contacttype",
        ["type"] = "contacttype",

        // Phone (alias — also recognized as "phone" for contacts import)
        ["phone"] = "phonenumber",
        ["phone number"] = "phonenumber",
        ["phonenumber"] = "phonenumber",
        ["mobile"] = "phonenumber",
        ["tel"] = "phonenumber",

        // Ledger fields
        ["ledgertype"] = "ledgertype",
        ["ledger type"] = "ledgertype",
        ["debt type"] = "ledgertype",
        ["entry type"] = "ledgertype",
        ["direction"] = "ledgertype",
    };

    private static string NormalizeHeader(string raw)
    {
        var lower = raw.ToLowerInvariant().Trim().Trim('"');
        return HeaderAliases.TryGetValue(lower, out var canonical) ? canonical : lower.Replace(" ", "");
    }

    public static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Strip currency symbols, commas, spaces
        var clean = value.Replace("₦", "").Replace("$", "").Replace("£", "")
                        .Replace(",", "").Replace(" ", "").Trim();
        // Use invariant culture to ensure consistent parsing regardless of server locale.
        if (decimal.TryParse(clean, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result) && result >= 0) return result;
        return null;
    }
}
