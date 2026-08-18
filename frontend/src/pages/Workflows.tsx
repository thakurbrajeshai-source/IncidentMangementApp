import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { Topbar } from "./Topbar";
import { api, type Workflow, type WorkflowSave, type WorkflowStep, type WorkflowInput,
  type WorkflowAuthType, type RunSummary, type RunDetail, type Incident } from "../api";
import { JsonTable } from "../components/JsonTable";

// Workflow builder (Admin/Resolver only). Three screens behind one page:
//   1. "Workflows"  – list + create/edit/run/delete definitions
//   2. "Run history"– every run with rendered per-step output tables
// Reporters never see this page; they get rendered output inline in the thread.

export function Workflows() {
  const { id } = useParams();
  const [tab, setTab] = useState<"workflows" | "runs">("workflows");
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [runs, setRuns] = useState<RunSummary[]>([]);
  const [editing, setEditing] = useState<Workflow | null | "new">(null);
  const [running, setRunning] = useState<Workflow | null>(null);
  const [runDetail, setRunDetail] = useState<RunDetail | null>(null);
    const nav = useNavigate();

  async function loadWorkflows() {
    setWorkflows(await api.workflows());
  }
  async function loadRuns() {
    setRuns(await api.workflowRuns());
  }
  useEffect(() => { loadWorkflows(); loadRuns(); }, []);

  async function del(w: Workflow) {
    if (!confirm(`Delete workflow "${w.name}"? This cannot be undone.`)) return;
    try {
      await api.deleteWorkflow(w.id);
      await loadWorkflows();
    } catch (e: any) { alert(e.message); }
  }

  async function openRunDetail(runId: string) {
    try {
      setRunDetail(await api.workflowRunDetail(runId));
    } catch (e: any) { alert(e.message); }
  }

  return (
    <div className="app">
      <Topbar title="Workflows" />
      <div className="container">

        <div className="tabs" style={{ marginBottom: 14 }}>
        <button onClick={() => nav(-1)}>← Back</button>

          <button className={tab === "workflows" ? "primary" : ""} onClick={() => setTab("workflows")}>Workflows</button>
          <button className={tab === "runs" ? "primary" : ""} onClick={async () => { setTab("runs"); await loadRuns(); }}>Run history</button>
          {tab === "workflows" && (
            <button className="accent" style={{ marginLeft: "auto" }} onClick={() => setEditing("new")}>+ New workflow</button>
          )}
        </div>

        {tab === "workflows" && (
          <table className="admin-table">
            <thead>
              <tr><th>Name</th><th>Steps</th><th>Inputs</th><th>Categories</th><th>Status</th><th>Created by</th><th>Actions</th></tr>
            </thead>
            <tbody>
              {workflows.map(w => (
                <tr key={w.id}>
                  <td>
                    <div style={{ fontWeight: 500 }}>{w.name}</div>
                    {w.description && <div style={{ fontSize: 11, color: "var(--text-soft)" }}>{w.description}</div>}
                  </td>
                  <td>{w.stepCount}</td>
                  <td>{w.inputCount}</td>
                  <td>
                    {w.categories && w.categories.length > 0
                      ? w.categories.map(c => c.name).join(", ")
                      : <span style={{ color: "var(--text-soft)", fontSize: 11 }}>—</span>}
                  </td>
                  <td>
                    <span className={`pill ${w.isActive ? "Success" : "Closed"}`}>{w.isActive ? "Active" : "Inactive"}</span>
                  </td>
                  <td style={{ color: "var(--text-soft)" }}>{w.createdByFullName}</td>
                  <td>
                    <button onClick={async () => { setEditing(await api.workflow(w.id)); }}>Edit</button>{" "}
                    <button className="accent" onClick={() => setRunning(w)}>Run</button>{" "}
                    <button className="danger" onClick={() => del(w)}>Delete</button>
                  </td>
                </tr>
              ))}
              {workflows.length === 0 && <tr><td colSpan={7} style={{ color: "var(--text-soft)" }}>No workflows yet — create one to get started.</td></tr>}
            </tbody>
          </table>
        )}

        {tab === "runs" && (
          <table className="admin-table">
            <thead>
              <tr><th>Workflow</th><th>Status</th><th>Incident</th><th>Triggered by</th><th>Started</th><th>Details</th></tr>
            </thead>
            <tbody>
              {runs.map(r => (
                <tr key={r.id}>
                  <td style={{ fontWeight: 500 }}>{r.workflowName}</td>
                  <td><span className={`pill ${runPill(r.status)}`}>{r.status}</span></td>
                  <td>{r.incidentTicketRef ?? "—"}</td>
                  <td style={{ color: "var(--text-soft)" }}>{r.triggeredByFullName}</td>
                  <td style={{ color: "var(--text-soft)" }}>{new Date(r.startedAt).toLocaleString()}</td>
                  <td><button onClick={() => openRunDetail(r.id)}>View</button></td>
                </tr>
              ))}
              {runs.length === 0 && <tr><td colSpan={6} style={{ color: "var(--text-soft)" }}>No runs yet.</td></tr>}
            </tbody>
          </table>
        )}
      </div>

      {editing && <BuilderModal workflow={editing === "new" ? null : editing} onClose={() => setEditing(null)}
        onSaved={async () => { setEditing(null); await loadWorkflows(); }} />}
      {running && <RunModal workflow={running} onClose={() => setRunning(null)}
        onRan={async (runId) => { setRunning(null); await loadRuns(); await openRunDetail(runId); }} />}
      {runDetail && <RunDetailModal run={runDetail} onClose={() => setRunDetail(null)} />}
    </div>
  );
}

// ----- Builder --------------------------------------------------------------

interface StepDraft extends WorkflowStep { headersText: string; }

function freshStep(): StepDraft {
  return { stepOrder: 1, name: "", httpMethod: "GET", urlTemplate: "", headers: {}, headersText: "{}",
    bodyTemplate: "", authType: "None", authConfig: {} };
}

function BuilderModal({ workflow, onClose, onSaved }: {
  workflow: Workflow | null; onClose: () => void; onSaved: () => void;
}) {
  const [name, setName] = useState(workflow?.name ?? "");
  const [description, setDescription] = useState(workflow?.description ?? "");
  const [isActive, setIsActive] = useState(workflow?.isActive ?? true);
  const [inputs, setInputs] = useState<WorkflowInput[]>(workflow?.inputs ?? []);
  const [steps, setSteps] = useState<StepDraft[]>(
    workflow?.steps?.map(s => ({ ...s, headersText: JSON.stringify(s.headers ?? {}) })) ?? [freshStep()]);
  const [selectedCategories, setSelectedCategories] = useState<number[]>(
    workflow?.categories?.map(c => c.id) ?? []);
  const [allCategories, setAllCategories] = useState<{ id: number; name: string }[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.categories().then(setAllCategories).catch(() => {}); }, []);

  function save() {
    setError(null);
    if (!name.trim()) { setError("Workflow name is required."); return; }
    if (steps.length === 0) { setError("Add at least one step."); return; }
    let payload: WorkflowSave;
    try {
      payload = {
        name: name.trim(),
        description: description.trim(),
        isActive,
        inputs,
        steps: steps.map((s, i) => {
          let headers: Record<string, string> = s.headers;
          try {
            const parsed: unknown = JSON.parse(s.headersText);
            if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) headers = parsed as Record<string, string>;
            else throw new Error("not an object");
          } catch {
            throw new Error(`Step ${i + 1} headers must be a valid JSON object.`);
          }
          if (!s.name.trim()) throw new Error(`Step ${i + 1} needs a name.`);
          if (!s.urlTemplate.trim()) throw new Error(`Step ${i + 1} is missing a URL.`);
          return { ...s, headers, stepOrder: i + 1 };
        }),
      };
    } catch (e: any) { setError(e.message); return; }

    setBusy(true);
    (async () => {
      try {
        let wfId: string;
        if (workflow) {
          await api.updateWorkflow(workflow.id, payload);
          wfId = workflow.id;
        } else {
          const r = await api.createWorkflow(payload);
          wfId = r.id;
        }
        // Save category assignments
        await api.setWorkflowCategories(wfId, selectedCategories);
        onSaved();
      } catch (e: any) { setError(e.message); setBusy(false); }
    })();
  }

  function toggleCategory(catId: number) {
    setSelectedCategories(prev =>
      prev.includes(catId) ? prev.filter(id => id !== catId) : [...prev, catId]
    );
  }

  return (
    <Modal title={workflow ? `Edit ${workflow.name}` : "New workflow"} onClose={onClose} wide>
      <div className="modal-scroll">
        <label>Name</label>
        <input value={name} onChange={e => setName(e.target.value)} placeholder="e.g. Fetch ticket status" />
        <label style={{ marginTop: 8 }}>Description</label>
        <input value={description} onChange={e => setDescription(e.target.value)} placeholder="What does this workflow do?" />
        <label style={{ marginTop: 8, display: "flex", alignItems: "center", gap: 6, color: "var(--text)" }}>
          <input type="checkbox" checked={isActive} onChange={e => setIsActive(e.target.checked)}
            style={{ width: "auto" }} /> Active
        </label>

        <p className="panel-title" style={{ marginTop: 12 }}>Inputs (asked when running)</p>
        {inputs.map((inp, i) => (
          <div key={i} className="step-row">
            <input placeholder="fieldName" value={inp.fieldName} style={{ flex: 1 }}
              onChange={e => setInputs(setAt(inputs, i, { ...inp, fieldName: e.target.value }))} />
            <input placeholder="Label" value={inp.label} style={{ flex: 1 }}
              onChange={e => setInputs(setAt(inputs, i, { ...inp, label: e.target.value }))} />
            <select value={inp.type} style={{ width: 90 }}
              onChange={e => setInputs(setAt(inputs, i, { ...inp, type: e.target.value }))}>
              <option value="text">text</option><option value="number">number</option><option value="date">date</option>
            </select>
            <label style={{ display: "flex", alignItems: "center", gap: 3, margin: 0, whiteSpace: "nowrap" }}>
              <input type="checkbox" checked={inp.required} style={{ width: "auto" }}
                onChange={e => setInputs(setAt(inputs, i, { ...inp, required: e.target.checked }))} /> Req
            </label>
            <button className="ghost danger" onClick={() => setInputs(inputs.filter((_, x) => x !== i))}>✕</button>
          </div>
        ))}
        <button className="ghost" onClick={() => setInputs([...inputs, { fieldName: "", label: "", type: "text", required: false }])}>+ Add input</button>

        <p className="panel-title" style={{ marginTop: 12 }}>Steps</p>
        {steps.map((s, i) => (
          <StepEditor key={i} step={s} index={i}
            canUp={i > 0} canDown={i < steps.length - 1}
            onChange={next => setSteps(setAt(steps, i, next))}
            onRemove={() => setSteps(steps.filter((_, x) => x !== i))}
            onMove={dir => setSteps(move(steps, i, dir))} />
        ))}
        <button className="ghost" onClick={() => setSteps([...steps, freshStep()])}>+ Add step</button>

        <p className="panel-title" style={{ marginTop: 12 }}>Default check for categories</p>
        <p style={{ fontSize: 11, color: "var(--text-soft)", margin: "0 0 6px" }}>
          When a reporter creates a ticket in a selected category, this workflow is offered automatically.
        </p>
        <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
          {allCategories.map(cat => (
            <label key={cat.id} style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 12, cursor: "pointer",
              padding: "4px 8px", borderRadius: "var(--radius)",
              border: `1px solid ${selectedCategories.includes(cat.id) ? "var(--teal)" : "var(--border)"}`,
              background: selectedCategories.includes(cat.id) ? "#f0fdfa" : "var(--surface)" }}>
              <input type="checkbox" checked={selectedCategories.includes(cat.id)}
                onChange={() => toggleCategory(cat.id)} style={{ width: "auto" }} />
              {cat.name}
            </label>
          ))}
        </div>

        {error && <div className="error">{error}</div>}
      </div>
      <div className="actions">
        <button onClick={onClose}>Cancel</button>
        <button className="primary" onClick={save} disabled={busy}>{workflow ? "Save changes" : "Create"}</button>
      </div>
    </Modal>
  );
}

function StepEditor({ step, index, canUp, canDown, onChange, onRemove, onMove }: {
  step: StepDraft; index: number; canUp: boolean; canDown: boolean;
  onChange: (s: StepDraft) => void; onRemove: () => void; onMove: (dir: -1 | 1) => void;
}) {
  const auth = step.authConfig ?? {};

  function authField(key: string, placeholder: string, type = "text") {
    return (
      <div key={key} style={{ marginTop: 6 }}>
        <label>{placeholder}</label>
        <input type={type} value={auth[key] ?? ""}
          onChange={e => onChange({ ...step, authConfig: { ...auth, [key]: e.target.value } })} />
      </div>
    );
  }

  return (
    <div className="card step-editor">
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <b style={{ fontSize: 12 }}>Step {index + 1}</b>
        <div>
          <button className="ghost" disabled={!canUp} onClick={() => onMove(-1)}>↑</button>
          <button className="ghost" disabled={!canDown} onClick={() => onMove(1)}>↓</button>
          <button className="ghost danger" onClick={onRemove}>Remove</button>
        </div>
      </div>
      <div style={{ display: "flex", gap: 6, marginTop: 8 }}>
        <select value={step.httpMethod} style={{ width: 110 }}
          onChange={e => onChange({ ...step, httpMethod: e.target.value })}>
          {["GET", "POST", "PUT", "PATCH", "DELETE"].map(m => <option key={m} value={m}>{m}</option>)}
        </select>
        <input placeholder="Step name" value={step.name} style={{ flex: 1 }}
          onChange={e => onChange({ ...step, name: e.target.value })} />
      </div>
      <input style={{ marginTop: 6 }} placeholder="URL template ({{input.x}} / {{stepN.response.path}})"
        value={step.urlTemplate} onChange={e => onChange({ ...step, urlTemplate: e.target.value })} />
      <input style={{ marginTop: 6 }} placeholder='Headers JSON, e.g. {"Accept":"application/json"}'
        value={step.headersText} onChange={e => onChange({ ...step, headersText: e.target.value })} />
      <textarea style={{ marginTop: 6 }} rows={2} placeholder="Body template (JSON, optional)"
        value={step.bodyTemplate} onChange={e => onChange({ ...step, bodyTemplate: e.target.value })} />
      <div style={{ marginTop: 6 }}>
        <select value={step.authType} style={{ width: 110 }}
          onChange={e => onChange({ ...step, authType: e.target.value as WorkflowAuthType })}>
          {(["None", "Bearer", "Basic", "ApiKey"] as const).map(a => <option key={a} value={a}>{a}</option>)}
        </select>
      </div>
      {step.authType === "Bearer" && authField("token", "Bearer token")}
      {step.authType === "Basic" && (
        <>
          {authField("username", "Username")}
          {authField("password", "Password", "password")}
        </>
      )}
      {step.authType === "ApiKey" && (
        <>
          {authField("header", "Header name (default X-API-Key)")}
          {authField("value", "Header value")}
        </>
      )}
    </div>
  );
}

// ----- Run ------------------------------------------------------------------

function RunModal({ workflow, onClose, onRan }: {
  workflow: Workflow; onClose: () => void; onRan: (runId: string) => void;
}) {
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [incidentId, setIncidentId] = useState("");
  const [values, setValues] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { api.allIncidents().then(setIncidents).catch(() => {}); }, []);

  async function run() {
    setError(null); setBusy(true);
    try {
      const r = await api.runWorkflow(workflow.id, incidentId || null, values);
      onRan(r.runId);
    } catch (e: any) { setError(e.message); setBusy(false); }
  }

  return (
    <Modal title={`Run ${workflow.name}`} onClose={onClose}>
      <label>Attach to incident</label>
      <select value={incidentId} onChange={e => setIncidentId(e.target.value)}>
        <option value="">— No incident (test run) —</option>
        {incidents.map(t => <option key={t.id} value={t.id}>{t.ticketRef} · {t.category.name}</option>)}
      </select>
      {(workflow.inputs ?? []).map(inp => (
        <div key={inp.fieldName} style={{ marginTop: 8 }}>
          <label>{inp.label}{inp.required && " *"}</label>
          <input value={values[inp.fieldName] ?? ""}
            onChange={e => setValues({ ...values, [inp.fieldName]: e.target.value })} />
        </div>
      ))}
      {error && <div className="error">{error}</div>}
      <div className="actions">
        <button onClick={onClose}>Cancel</button>
        <button className="primary" onClick={run} disabled={busy}>Run workflow</button>
      </div>
    </Modal>
  );
}

// ----- Run detail -----------------------------------------------------------

function RunDetailModal({ run, onClose }: { run: RunDetail; onClose: () => void }) {
  return (
    <Modal title={`${run.workflowName} · ${run.status}`} onClose={onClose} wide>
      <p style={{ fontSize: 12, color: "var(--text-soft)", margin: "0 0 10px" }}>
        {run.incidentTicketRef && <>Incident {run.incidentTicketRef} · </>}
        Triggered by {run.triggeredByFullName} · {new Date(run.startedAt).toLocaleString()}
        {run.completedAt && <> · finished {new Date(run.completedAt).toLocaleString()}</>}
      </p>
      {run.errorMessage && <div className="error" style={{ marginBottom: 8 }}>Failed at step {run.failedStepOrder}: {run.errorMessage}</div>}
      <div className="modal-scroll">
        {run.steps.map(s => (
          <div key={s.stepOrder} className="card" style={{ marginBottom: 8 }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 6 }}>
              <b style={{ fontSize: 12 }}>Step {s.stepOrder}: {s.stepName}</b>
              <span style={{ fontSize: 11, color: "var(--text-soft)" }}>
                HTTP {s.statusCode ?? "—"} · {s.succeeded ? "ok" : "failed"}
              </span>
            </div>
            {s.errorMessage && <div className="error">{s.errorMessage}</div>}
            <JsonTable table={s.table} />
          </div>
        ))}
      </div>
      <div className="actions">
        <button onClick={onClose}>Close</button>
      </div>
    </Modal>
  );
}

// ----- Helpers --------------------------------------------------------------

function Modal({ title, children, onClose, wide }: {
  title: string; children: React.ReactNode; onClose: () => void; wide?: boolean;
}) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className={`modal ${wide ? "wide" : ""}`} onClick={e => e.stopPropagation()}>
        <h3>{title}</h3>
        {children}
      </div>
    </div>
  );
}

function setAt<T>(arr: T[], i: number, v: T): T[] { return arr.map((x, j) => (j === i ? v : x)); }

function move<T>(arr: T[], i: number, dir: -1 | 1): T[] {
  const j = i + dir;
  if (j < 0 || j >= arr.length) return arr;
  const next = [...arr];
  [next[i], next[j]] = [next[j], next[i]];
  return next;
}

function runPill(status: string): string {
  if (status === "Success") return "Success";
  if (status === "Failed") return "Failed";
  return "Running";
}
