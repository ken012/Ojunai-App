import { getStoredUser } from "./auth";

export const Permission = {
  RecordSales: "record_sales",
  RecordExpenses: "record_expenses",
  ManageStock: "manage_stock",
  ViewStock: "view_stock",
  VoidSales: "void_sales",
  ViewAllReports: "view_all_reports",
  ViewOwnReports: "view_own_reports",
  ManageStaff: "manage_staff",
  ManageSettings: "manage_settings",
  ManageDebts: "manage_debts",
} as const;

const RolePermissions: Record<string, Set<string>> = {
  Owner: new Set(Object.values(Permission)),
  Admin: new Set([
    Permission.RecordSales, Permission.RecordExpenses, Permission.ManageStock,
    Permission.ViewStock, Permission.VoidSales, Permission.ViewAllReports,
    Permission.ViewOwnReports, Permission.ManageStaff, Permission.ManageDebts,
  ]),
  Sales: new Set([
    Permission.RecordSales, Permission.ViewStock, Permission.ViewOwnReports,
  ]),
  Bookkeeper: new Set([
    Permission.RecordExpenses, Permission.ViewAllReports, Permission.ManageDebts, Permission.ViewStock,
  ]),
  Viewer: new Set([
    Permission.ViewAllReports, Permission.ViewStock,
  ]),
};

export function hasPermission(permission: string): boolean {
  if (typeof window === "undefined") return true; // SSR — allow all, enforce on client
  const user = getStoredUser();
  if (!user?.role) return false;
  return RolePermissions[user.role]?.has(permission) ?? false;
}

export function getUserRole(): string {
  if (typeof window === "undefined") return "Owner";
  const user = getStoredUser();
  return user?.role ?? "Owner";
}
