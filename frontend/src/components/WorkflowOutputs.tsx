import { useEffect, useState } from "react";
import { api, type IncidentRunOutput, type User } from "../api";
import { JsonTable } from "./JsonTable";

// Renders every workflow run attached to an incident inline in the ticket
// thread. Reporters see this (rendered tables only — never raw JSON).
// Respects VisibleInComments flag from WorkflowIncidentAssignment.
export function WorkflowOutputs({ incidentId, role }: { incidentId: string; role?: string }) {
  const [outputs, setOutputs] = useState<IncidentRunOutput[] | null>(null);
  const [runCount, setRunCount] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const o = await api.incidentWorkflowOutputs(incidentId);
      setOutputs(o);
    } catch { setOutputs([]); }
    // Run count for current user (PRD 6b)
    try {
      const rc = await api.runCount(incidentId);
      setRunCount(rc.runCount);
    } catch { setRunCount(0); }
  }

  useEffect(() => { load(); }, [incidentId]);

  async function toggleVisibility(workflowId: string, visible: boolean) {
    setBusy(true);
    try {
      await api.setWorkflowVisibility(incidentId, workflowId, visible);
      await load();
    } finally { setBusy(false); }
  }

  if (!outputs || outputs.length === 0) return null;

  return (
    <div className="card" style={{ marginTop: 12 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h4 style={{ margin: 0 }}>Workflow runs</h4>
        {runCount !== null && runCount > 0 && (
          <span style={{ fontSize: 11, color: "var(--text-soft)" }}>
            You've run {runCount} check{runCount !== 1 ? "s" : ""} on this ticket.
          </span>
        )}
      </div>
      {outputs.map(r => (
        <div key={r.runId} className="run-outputs" style={{ marginBottom: 12 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
            <b style={{ fontSize: 12 }}>{r.workflowName}</b>
            <span className={`pill ${runPill(r.status)}`}>{r.status}</span>
            {(role === "Resolver" || role === "Admin") && (
              <label style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 11, color: "var(--text-soft)", cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={r.visibleInComments}
                  disabled={busy}
                  onChange={e => toggleVisibility(r.runId, e.target.checked)}
                  style={{ width: "auto" }}
                />
                Visible in thread
              </label>
            )}
          </div>
          <div style={{ fontSize: 11, color: "var(--text-soft)", margin: "2px 0 6px" }}>
            {new Date(r.startedAt).toLocaleString()} · by {r.triggeredByFullName}
          </div>
          {r.steps.map(s => (
            <div key={s.stepOrder} style={{ marginBottom: 8 }}>
              <div style={{ fontSize: 11, fontWeight: 500 }}>
                Step {s.stepOrder}: {s.stepName}
                {s.succeeded && s.statusCode ? ` (HTTP ${s.statusCode})` : ""}
              </div>
              {s.errorMessage && <div className="error">{s.errorMessage}</div>}
              <JsonTable table={s.table} />
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

function runPill(status: string): string {
  if (status === "Success") return "Success";
  if (status === "Failed") return "Failed";
  return "Running";
}
