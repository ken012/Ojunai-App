using System.Text;

namespace Ojunai.API.Common;

public static class CsvParser
{
    public static List<Dictionary<string, string>> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();

        // Strip BOM
        if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1);

        // Normalize line endings inside the doc (the row tokenizer below handles bare \n;
        // \r\n collapses to \n, lone \r becomes \n).
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        if (string.IsNullOrEmpty(content)) return new List<Dictionary<string, string>>();

        // Auto-detect delimiter (comma vs semicolon) from the first physical line, ignoring
        // anything inside quotes \u2014 a header rarely contains quoted newlines but we still
        // want to count delimiters in the header context only.
        var firstLineEnd = FindUnquotedNewline(content, 0);
        var headerSpan = firstLineEnd < 0 ? content : content[..firstLineEnd];
        var delimiter = CountUnquoted(headerSpan, ';') > CountUnquoted(headerSpan, ',') ? ';' : ',';

        // RFC 4180-ish row tokenizer: walk the entire string with quote-state, emit fields
        // on unquoted commas/semicolons, emit rows on unquoted newlines. Quoted fields can
        // contain commas, semicolons, AND newlines. Doubled quotes ("") inside a quoted
        // field collapse to a single quote.
        var rowsRaw = TokenizeRows(content, delimiter);
        if (rowsRaw.Count < 2) return new List<Dictionary<string, string>>();

        var headers = rowsRaw[0]
            .Select(h => NormalizeHeader(h.Trim()))
            .ToArray();

        if (headers.Length > 50) return new List<Dictionary<string, string>>();

        var results = new List<Dictionary<string, string>>();
        for (int i = 1; i < rowsRaw.Count; i++)
        {
            var values = rowsRaw[i];
            // Skip blank rows (a row whose every cell is whitespace).
            if (values.All(v => string.IsNullOrWhiteSpace(v))) continue;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < headers.Length && j < values.Count; j++)
            {
                var val = values[j].Trim();
                if (!string.IsNullOrEmpty(val)) row[headers[j]] = val;
            }
            if (row.Count > 0) results.Add(row);
        }

        return results;
    }

    /// <summary>
    /// Streaming tokenizer that splits an entire CSV document into rows of fields, honouring
    /// RFC 4180 quoting (including newlines inside quoted fields and doubled-quote escapes).
    /// </summary>
    private static List<List<string>> TokenizeRows(string content, char delimiter)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        // Escaped quote inside a quoted field.
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentField.Append(c);
                }
                continue;
            }

            // Outside quotes:
            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();
            }
            else if (c == '\n')
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();
                rows.Add(currentRow);
                currentRow = new List<string>();
            }
            else
            {
                currentField.Append(c);
            }
        }

        // Flush trailing field/row if the file doesn't end with a newline.
        if (currentField.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentField.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static int FindUnquotedNewline(string s, int start)
    {
        bool inQuotes = false;
        for (int i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (c == '\n' && !inQuotes) return i;
        }
        return -1;
    }

    private static int CountUnquoted(string s, char target)
    {
        bool inQuotes = false;
        var n = 0;
        foreach (var c in s)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == target && !inQuotes) n++;
        }
        return n;
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

        // Sourcing (Phase 1)
        ["sku"] = "sku",
        ["item code"] = "sku",
        ["product code"] = "sku",
        ["sku code"] = "sku",
        ["barcode"] = "barcode",
        ["bar code"] = "barcode",
        ["ean"] = "barcode",
        ["upc"] = "barcode",
        ["supplier"] = "supplier",
        ["supplier name"] = "supplier",
        ["vendor"] = "supplier",
        ["leadtime"] = "leadtime",
        ["lead time"] = "leadtime",
        ["lead days"] = "leadtime",
        ["lead time (days)"] = "leadtime",
        ["lead time days"] = "leadtime",

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

        // PaymentMethod
        ["paymentmethod"] = "paymentmethod",
        ["payment method"] = "paymentmethod",
        ["method"] = "paymentmethod",

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
