import { api } from "./api";

export interface AlertDto {
  id: string;
  type: string;
  severity: "Info" | "Warning" | "Critical";
  /** "Business" = operational alert visible to Owner/Admin only. "Personal" = your own account. */
  scope: "Business" | "Personal";
  title: string;
  body: string;
  linkUrl?: string | null;
  createdAtUtc: string;
  readAtUtc?: string | null;
}

export async function listAlerts(opts: { unreadOnly?: boolean; limit?: number } = {}): Promise<AlertDto[]> {
  const params = new URLSearchParams();
  if (opts.unreadOnly) params.set("unreadOnly", "true");
  if (opts.limit) params.set("limit", String(opts.limit));
  const { data } = await api.get<{ data: AlertDto[] }>(`/alerts${params.toString() ? `?${params}` : ""}`);
  return data.data ?? [];
}

export async function unreadAlertCount(): Promise<number> {
  const { data } = await api.get<{ data: { count: number } }>("/alerts/unread-count");
  return data.data?.count ?? 0;
}

export async function markAlertRead(id: string): Promise<void> {
  await api.post(`/alerts/${id}/read`);
}

export async function dismissAlert(id: string): Promise<void> {
  await api.post(`/alerts/${id}/dismiss`);
}

export async function markAllAlertsRead(): Promise<void> {
  await api.post("/alerts/mark-all-read");
}
