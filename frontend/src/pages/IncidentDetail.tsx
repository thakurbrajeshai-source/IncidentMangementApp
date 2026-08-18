import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { Topbar } from "./Topbar";
import { api, type Incident, type User, type AvailableWorkflow, type WorkflowInput, type DefaultWorkflow } from "../api";
import { WorkflowOutputs } from "../components/WorkflowOutputs";
import { MentionInput } from "../components/MentionInput";

export function IncidentDetail() {
  const { id } = useParams();
  const [incident, setIncident] = useState<Incident | null>(null);
  const [users, setUsers] = useState<User[]>([]);
  const [msg, setMsg] = useState("");
  const [tagged, setTagged] = useState<string[]>([]);
  const [attachPicker, setAttachPicker] = useState(false);
  const [attachBusy, setAttachBusy] = useState(false);
  const me = JSON.parse(localStorage.getItem("im_user")!);
  const nav = useNavigate();

  async function load() {
    if (!id) return;
    setIncident(await api.incident(id));
  }
  useEffect(() => { load(); api.users().then(setUsers); }, [id]);

  if (!incident) return <div className="app"><Topbar title="Loading…" /><div className="container">Loading…</div></div>;

  const canResolve = me.role !== "Reporter" && incident.currentAssigneeId === me.id
    && (incident.status === "InProgress" || incident.status === "Reopened");
  const canConfirm = me.id === incident.reporterId && incident.status === "Resolved";
  const canReopen  = me.id === incident.reporterId && incident.status === "Resolved";
  const canReject  = me.role === "Admin" && incident.status !== "Closed" && incident.status !== "Rejected";
  const canForceClose = me.role === "Admin" && incident.status !== "Closed" && incident.status !== "Rejected";
  const canAttach = me.role === "Resolver" || me.role === "Admin";

  return (
    <div className="app">
      <Topbar title={`${incident.ticketRef}`} />
      <div className="container">
        <button onClick={() => nav(-1)}>← Back</button>
        <div className="card" style={{ marginTop: 12 }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <div>
              <div style={{ fontWeight: 500 }}>{incident.ticketRef} · {incident.category.name}</div>
              <div style={{ color: "var(--text-soft)", fontSize: 12 }}>
                Reported by {incident.reporter?.fullName} · {new Date(incident.createdAt).toLocaleString()}
                {incident.currentAssignee && <> · Assigned to {incident.currentAssignee.fullName}</>}
              </div>
            </div>
            <span className={`pill ${incident.status === "Reopened" ? "InProgress" : incident.status}`}>{incident.status}</span>
          </div>
          <p style={{ marginTop: 12 }}>{incident.description}</p>
          {incident.rejectionReason && <p style={{ color: "var(--danger)", fontSize: 12 }}>Rejection: {incident.rejectionReason}</p>}
          <div style={{ display: "flex", gap: 8, marginTop: 8 }}>
            {canResolve && <button className="primary" onClick={async () => { await api.resolve(incident.id); await load(); }}>✓ Mark resolved</button>}
            {canConfirm && <button className="primary" onClick={async () => { await api.confirm(incident.id); await load(); }}>Confirm fixed</button>}
            {canReopen  && <button className="danger"  onClick={async () => { await api.reopen(incident.id); await load(); }}>Reopen</button>}
            {canReject  && <button className="danger"  onClick={async () => {
              const r = prompt("Reject reason?");
              if (r && r.trim()) { await api.reject(incident.id, r.trim()); await load(); }
            }}>Reject</button>}
            {canForceClose && <button onClick={async () => {
              if (confirm(`Force-close ${incident.ticketRef}?`)) { await api.forceClose(incident.id); await load(); }
            }}>Force close</button>}
          </div>
        </div>

        {/* Reporter default workflow trigger card */}
        {me.role === "Reporter" && incident.reporterId === me.id && incident.status === "Open" && (
          <ReporterWorkflowCard incidentId={incident.id} categoryId={incident.categoryId} onRun={load} />
        )}

        {/* Workflow outputs (visibility-aware) */}
        <WorkflowOutputs incidentId={incident.id} role={me.role} />

        {/* Attach another check (Resolver/Admin + reporter on own open ticket) */}
        {(canAttach || (me.role === "Reporter" && incident.reporterId === me.id)) && (
          <div className="card" style={{ marginTop: 12 }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h4 style={{ margin: 0 }}>Attach a check</h4>
              <button className="accent" onClick={() => setAttachPicker(!attachPicker)}>
                {attachPicker ? "Cancel" : "+ Attach workflow"}
              </button>
            </div>
            {attachPicker && (
              <AttachWorkflowPicker
                incidentId={incident.id}
                onAttached={async () => { setAttachPicker(false); await load(); }}
              />
            )}
          </div>
        )}

        {/* Conversation thread */}
        <div className="card" style={{ marginTop: 12 }}>
          <h4 style={{ margin: "0 0 8px" }}>Conversation</h4>
          <div className="thread">
            {incident.comments?.map(c => {
              const isMine = c.authorId === me.id;
              return (
                <div key={c.id} className={`bubble ${isMine ? "out" : "in"}`}>
                  <div style={{ fontSize: 10, color: "var(--text-soft)" }}>{isMine ? "You" : c.author?.fullName}</div>
                  {c.message}
                </div>
              );
            })}
          </div>
          <div style={{ marginTop: 10 }}>
            <MentionInput
              users={users} meId={me.id}
              value={msg} onChange={setMsg}
              onSend={async () => {
                if (!msg.trim()) return;
                await api.addComment(incident.id, msg, tagged);
                setMsg(""); setTagged([]);
                await load();
              }}
              tagged={tagged} onTaggedChange={setTagged}
              placeholder="Reply or @tag someone…"
            />
          </div>
        </div>
      </div>
    </div>
  );
}

// ----- Reporter default workflow trigger card --------------------------------

function ReporterWorkflowCard({ incidentId, categoryId, onRun }: {
  incidentId: string; categoryId: number; onRun: () => void;
}) {
  const [workflow, setWorkflow] = useState<DefaultWorkflow | null>(null);
  const [values, setValues] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);

  useEffect(() => {
    api.defaultWorkflow(categoryId).then(w => setWorkflow(w)).catch(() => {});
  }, [categoryId]);

  if (!workflow) return null;

  async function run() {
    if (!workflow) return;
    setBusy(true);
    try {
      await api.runWorkflowOnTicket(incidentId, workflow.id, values);
      setResult("Workflow triggered! Check the thread for results.");
      await onRun();
    } catch (e: any) { setResult(`Error: ${e.message}`); }
    finally { setBusy(false); }
  }

  return (
    <div className="card" style={{ marginTop: 12, borderLeft: "3px solid var(--teal)" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <b style={{ fontSize: 13 }}>Run: {workflow.name}</b>
          {workflow.description && <div style={{ fontSize: 11, color: "var(--text-soft)" }}>{workflow.description}</div>}
        </div>
      </div>
      {workflow.inputs.length > 0 && (
        <div style={{ marginTop: 8 }}>
          {workflow.inputs.map(inp => (
            <div key={inp.fieldName} style={{ marginBottom: 6 }}>
              <label>{inp.label}{inp.required && " *"}</label>
              <input
                value={values[inp.fieldName] ?? ""}
                onChange={e => setValues({ ...values, [inp.fieldName]: e.target.value })}
                placeholder={inp.label}
              />
            </div>
          ))}
        </div>
      )}
      <div style={{ display: "flex", gap: 8, marginTop: 8, alignItems: "center" }}>
        <button className="primary" onClick={run} disabled={busy}>
          {busy ? "Running…" : "Run check"}
        </button>
        {result && <span style={{ fontSize: 11, color: result.startsWith("Error") ? "var(--danger)" : "var(--mint)" }}>{result}</span>}
      </div>
    </div>
  );
}

// ----- Attach workflow picker -----------------------------------------------

function AttachWorkflowPicker({ incidentId, onAttached }: {
  incidentId: string; onAttached: () => void;
}) {
  const [workflows, setWorkflows] = useState<AvailableWorkflow[]>([]);
  const [selected, setSelected] = useState<string>("");
  const [inputs, setInputs] = useState<Record<string, string>>({});
  const [inputDefs, setInputDefs] = useState<WorkflowInput[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { api.availableWorkflows().then(setWorkflows).catch(() => {}); }, []);

  async function loadInputs(wfId: string) {
    setSelected(wfId);
    setInputs({});
    setError(null);
    if (!wfId) { setInputDefs([]); return; }
    try {
      const w = await api.workflow(wfId);
      setInputDefs(w.inputs ?? []);
    } catch { setInputDefs([]); }
  }

  async function attach() {
    if (!selected) return;
    setBusy(true); setError(null);
    try {
      await api.attachWorkflow(incidentId, selected, inputs);
      onAttached();
    } catch (e: any) { setError(e.message); setBusy(false); }
  }

  return (
    <div style={{ marginTop: 10 }}>
      <label>Select workflow</label>
      <select value={selected} onChange={e => loadInputs(e.target.value)}>
        <option value="">— Choose a workflow —</option>
        {workflows.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
      </select>
      {inputDefs.length > 0 && (
        <div style={{ marginTop: 8 }}>
          {inputDefs.map(inp => (
            <div key={inp.fieldName} style={{ marginBottom: 6 }}>
              <label>{inp.label}{inp.required && " *"}</label>
              <input
                value={inputs[inp.fieldName] ?? ""}
                onChange={e => setInputs({ ...inputs, [inp.fieldName]: e.target.value })}
                placeholder={inp.label}
              />
            </div>
          ))}
        </div>
      )}
      {error && <div className="error">{error}</div>}
      <button className="primary" style={{ marginTop: 8 }} onClick={attach} disabled={busy || !selected}>
        {busy ? "Attaching…" : "Attach & Run"}
      </button>
    </div>
  );
}
