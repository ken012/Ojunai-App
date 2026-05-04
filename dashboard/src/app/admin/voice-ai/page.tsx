"use client";

import { useState, useEffect, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { api } from "@/lib/api";

type VoiceAIBusiness = {
  id: string;
  name: string;
  accountNumber: string;
  voiceAIPlanStatus: string;
  voiceAIInternalOverride: boolean;
  voiceAIEnabledAt: string | null;
  voiceAITrialEndsAt: string | null;
};

type Overview = {
  totalEnabled: number;
  byStatus: { status: string; count: number }[];
  overrides: number;
  businesses: VoiceAIBusiness[];
};

function VoiceAIAdminContent() {
  const searchParams = useSearchParams();
  const key = searchParams.get("key");

  const [overview, setOverview] = useState<Overview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Enable form
  const [businessId, setBusinessId] = useState("");
  const [action, setAction] = useState<"active" | "trial" | "suspended" | "inactive" | "override-on" | "override-off">("active");
  const [updating, setUpdating] = useState(false);
  const [result, setResult] = useState<string | null>(null);

  async function fetchOverview() {
    if (!key) return;
    setLoading(true);
    try {
      const { data } = await api.get(`/admin/voice-ai/overview?key=${key}`);
      setOverview(data as Overview);
      setError(null);
    } catch {
      setError("Failed to load. Check your admin key.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { fetchOverview(); }, [key]); // eslint-disable-line react-hooks/exhaustive-deps

  async function handleUpdate() {
    if (!key || !businessId.trim()) return;
    setUpdating(true);
    setResult(null);
    try {
      const body: { planStatus?: string; internalOverride?: boolean } = {};
      if (action === "override-on") body.internalOverride = true;
      else if (action === "override-off") body.internalOverride = false;
      else body.planStatus = action;

      const { data } = await api.patch(`/admin/business/${businessId.trim()}/voice-ai?key=${key}`, body);
      const d = data as { businessName?: string; voiceAIPlanStatus?: string; voiceAIInternalOverride?: boolean };
      setResult(`Updated: ${d.businessName} — status: ${d.voiceAIPlanStatus}, override: ${d.voiceAIInternalOverride}`);
      fetchOverview();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { error?: string } } };
      setResult(`Error: ${ax.response?.data?.error ?? "Failed"}`);
    } finally {
      setUpdating(false);
    }
  }

  if (!key) return <p style={{ padding: 40, color: "#666" }}>Missing admin key. Add ?key=YOUR_KEY to the URL.</p>;

  return (
    <div style={{ maxWidth: 800, margin: "0 auto", padding: 40, fontFamily: "system-ui, sans-serif" }}>
      <h1 style={{ fontSize: 24, fontWeight: 700, marginBottom: 4 }}>Voice AI Admin</h1>
      <p style={{ color: "#666", fontSize: 14, marginBottom: 32 }}>Enable, disable, and manage Voice AI for businesses</p>

      {/* Enable / Update */}
      <div style={{ background: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: 12, padding: 24, marginBottom: 32 }}>
        <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 16 }}>Update Business</h2>
        <div style={{ display: "flex", gap: 12, flexWrap: "wrap", alignItems: "end" }}>
          <div>
            <label style={{ display: "block", fontSize: 12, color: "#64748b", marginBottom: 4 }}>Business ID</label>
            <input
              type="text"
              value={businessId}
              onChange={(e) => setBusinessId(e.target.value)}
              placeholder="Paste business GUID"
              style={{ height: 36, padding: "0 10px", border: "1px solid #e2e8f0", borderRadius: 6, fontSize: 13, width: 300 }}
            />
          </div>
          <div>
            <label style={{ display: "block", fontSize: 12, color: "#64748b", marginBottom: 4 }}>Action</label>
            <select
              value={action}
              onChange={(e) => setAction(e.target.value as typeof action)}
              style={{ height: 36, padding: "0 8px", border: "1px solid #e2e8f0", borderRadius: 6, fontSize: 13 }}
            >
              <option value="active">Set Active</option>
              <option value="trial">Start Trial</option>
              <option value="suspended">Suspend</option>
              <option value="inactive">Deactivate</option>
              <option value="override-on">Override ON (bypass billing)</option>
              <option value="override-off">Override OFF</option>
            </select>
          </div>
          <button
            onClick={handleUpdate}
            disabled={updating || !businessId.trim()}
            style={{
              height: 36, padding: "0 20px", background: "#06b6d4", color: "white", border: "none",
              borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: "pointer", opacity: updating ? 0.6 : 1
            }}
          >
            {updating ? "Updating..." : "Apply"}
          </button>
        </div>
        {result && (
          <p style={{ marginTop: 12, fontSize: 13, color: result.startsWith("Error") ? "#dc2626" : "#059669" }}>{result}</p>
        )}
      </div>

      {/* Overview */}
      {loading && <p style={{ color: "#94a3b8" }}>Loading...</p>}
      {error && <p style={{ color: "#dc2626" }}>{error}</p>}

      {overview && (
        <>
          <div style={{ display: "flex", gap: 16, marginBottom: 24 }}>
            <div style={{ background: "#f0fdf4", padding: 16, borderRadius: 8, flex: 1, textAlign: "center" }}>
              <p style={{ fontSize: 24, fontWeight: 700, color: "#059669" }}>{overview.totalEnabled}</p>
              <p style={{ fontSize: 12, color: "#64748b" }}>Total Enabled</p>
            </div>
            <div style={{ background: "#fef3c7", padding: 16, borderRadius: 8, flex: 1, textAlign: "center" }}>
              <p style={{ fontSize: 24, fontWeight: 700, color: "#d97706" }}>{overview.overrides}</p>
              <p style={{ fontSize: 12, color: "#64748b" }}>Internal Overrides</p>
            </div>
            {overview.byStatus.map((s) => (
              <div key={s.status} style={{ background: "#f1f5f9", padding: 16, borderRadius: 8, flex: 1, textAlign: "center" }}>
                <p style={{ fontSize: 24, fontWeight: 700, color: "#334155" }}>{s.count}</p>
                <p style={{ fontSize: 12, color: "#64748b" }}>{s.status}</p>
              </div>
            ))}
          </div>

          {overview.businesses.length > 0 ? (
            <table style={{ width: "100%", fontSize: 13, borderCollapse: "collapse" }}>
              <thead>
                <tr style={{ borderBottom: "2px solid #e2e8f0", textAlign: "left" }}>
                  <th style={{ padding: "8px 4px", color: "#64748b", fontWeight: 500 }}>Business</th>
                  <th style={{ padding: "8px 4px", color: "#64748b", fontWeight: 500 }}>Account #</th>
                  <th style={{ padding: "8px 4px", color: "#64748b", fontWeight: 500 }}>Status</th>
                  <th style={{ padding: "8px 4px", color: "#64748b", fontWeight: 500 }}>Override</th>
                  <th style={{ padding: "8px 4px", color: "#64748b", fontWeight: 500 }}>Enabled At</th>
                </tr>
              </thead>
              <tbody>
                {overview.businesses.map((b) => (
                  <tr key={b.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                    <td style={{ padding: "8px 4px" }}>
                      <span style={{ fontWeight: 500 }}>{b.name}</span>
                      <br />
                      <span style={{ fontSize: 10, color: "#94a3b8", fontFamily: "monospace" }}>{b.id}</span>
                    </td>
                    <td style={{ padding: "8px 4px", fontFamily: "monospace" }}>{b.accountNumber}</td>
                    <td style={{ padding: "8px 4px" }}>
                      <span style={{
                        padding: "2px 8px", borderRadius: 99, fontSize: 11, fontWeight: 600,
                        background: b.voiceAIPlanStatus === "active" ? "#dcfce7" : b.voiceAIPlanStatus === "trial" ? "#fef3c7" : "#fee2e2",
                        color: b.voiceAIPlanStatus === "active" ? "#166534" : b.voiceAIPlanStatus === "trial" ? "#92400e" : "#991b1b",
                      }}>
                        {b.voiceAIPlanStatus}
                      </span>
                    </td>
                    <td style={{ padding: "8px 4px" }}>{b.voiceAIInternalOverride ? "Yes" : "—"}</td>
                    <td style={{ padding: "8px 4px", fontSize: 12, color: "#64748b" }}>
                      {b.voiceAIEnabledAt ? new Date(b.voiceAIEnabledAt).toLocaleDateString() : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <p style={{ color: "#94a3b8", textAlign: "center", padding: 32 }}>No businesses have Voice AI enabled yet.</p>
          )}
        </>
      )}
    </div>
  );
}

export default function VoiceAIAdminPage() {
  return (
    <Suspense fallback={<p style={{ padding: 40, color: "#94a3b8" }}>Loading...</p>}>
      <VoiceAIAdminContent />
    </Suspense>
  );
}
