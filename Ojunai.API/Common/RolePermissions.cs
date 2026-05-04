using Ojunai.API.Models;

namespace Ojunai.API.Common;

public static class Permission
{
    public const string RecordSales = "record_sales";
    public const string RecordExpenses = "record_expenses";
    public const string ManageStock = "manage_stock";
    public const string ViewStock = "view_stock";
    public const string VoidSales = "void_sales";
    public const string ViewAllReports = "view_all_reports";
    public const string ViewOwnReports = "view_own_reports";
    public const string ManageStaff = "manage_staff";
    public const string ManageSettings = "manage_settings";
    public const string ManageDebts = "manage_debts";
}

public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<string>> Permissions = new()
    {
        [UserRole.Owner] = new HashSet<string>
        {
            Permission.RecordSales, Permission.RecordExpenses, Permission.ManageStock,
            Permission.ViewStock, Permission.VoidSales, Permission.ViewAllReports,
            Permission.ViewOwnReports, Permission.ManageStaff, Permission.ManageSettings,
            Permission.ManageDebts
        },
        [UserRole.Admin] = new HashSet<string>
        {
            Permission.RecordSales, Permission.RecordExpenses, Permission.ManageStock,
            Permission.ViewStock, Permission.VoidSales, Permission.ViewAllReports,
            Permission.ViewOwnReports, Permission.ManageStaff, Permission.ManageDebts
        },
        [UserRole.Sales] = new HashSet<string>
        {
            Permission.RecordSales, Permission.ViewStock, Permission.ViewOwnReports
        },
        [UserRole.Bookkeeper] = new HashSet<string>
        {
            Permission.RecordExpenses, Permission.ViewAllReports, Permission.ManageDebts, Permission.ViewStock
        },
        [UserRole.Viewer] = new HashSet<string>
        {
            Permission.ViewAllReports, Permission.ViewStock
        }
    };

    public static bool HasPermission(UserRole role, string permission)
        => Permissions.TryGetValue(role, out var perms) && perms.Contains(permission);

    public static HashSet<string> GetPermissions(UserRole role)
        => Permissions.TryGetValue(role, out var perms) ? perms : new HashSet<string>();

    public static readonly string[] AllRoles = { "Owner", "Admin", "Sales", "Bookkeeper", "Viewer" };
}
