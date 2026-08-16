import { useEffect, useState } from "react";
import { api, type IncidentRunOutput } from "../api";
import { JsonTable } from "./JsonTable";

// Renders every workflow run attached to an incident inline in the ticket
// thread. Reporters see this (rendered tables only — never raw JSON).
export function WorkflowOutputs({ incidentId }: { incidentId: string }) {
  const [outputs, setOutputs] = useState<IncidentRunOutput[] | null>(null);

  useEffect(() => {
    let alive = true;
    api.incidentWorkflowOutputs(incidentId)
      .then(o => { if (alive) setOutputs(o); })
      .catch(() => { if (alive) setOutputs([]); });
    return () => { alive = false; };
  }, [incidentId]);

  if (!outputs || outputs.length === 0) return null;

  return (
    <div className="card" style={{ marginTop: 12 }}>
      <h4 style={{ margin: "0 0 8px" }}>Workflow runs</h4>
      {outputs.map(r => (
        <div key={r.runId} className="run-outputs" style={{ marginBottom: 12 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <b style={{ fontSize: 12 }}>{r.workflowName}</b>
            <span className={`pill ${runPill(r.status)}`}>{r.status}</span>
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
