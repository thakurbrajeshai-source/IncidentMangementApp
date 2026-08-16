import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { Topbar } from "./Topbar";
import { api, type Incident, type User } from "../api";
import { WorkflowOutputs } from "../components/WorkflowOutputs";

// Shared incident detail / thread view. Used by Resolver right panel
// (link) and Admin (Join chat) and Reporter (tap a card).
export function IncidentDetail() {
  const { id } = useParams();
  const [incident, setIncident] = useState<Incident | null>(null);
  const [users, setUsers] = useState<User[]>([]);
  const [msg, setMsg] = useState("");
  const [tagged, setTagged] = useState<string[]>([]);
  const [tagInput, setTagInput] = useState("");
  const me = JSON.parse(localStorage.getItem("im_user")!);
  const nav = useNavigate();

  async function load() {
    if (!id) return;
    setIncident(await api.incident(id));
  }
  useEffect(() => { load(); api.users().then(setUsers); }, [id]);

  async function send() {
    if (!msg.trim() || !id) return;
    await api.addComment(id, msg, tagged);
    setMsg(""); setTagged([]);
    await load();
  }

  if (!incident) return <div className="app"><Topbar title="Loading…" /><div className="container">Loading…</div></div>;

  const canResolve = me.role !== "Reporter" && incident.currentAssigneeId === me.id
    && (incident.status === "InProgress" || incident.status === "Reopened");
  const canConfirm = me.id === incident.reporterId && incident.status === "Resolved";
  const canReopen  = me.id === incident.reporterId && incident.status === "Resolved";
  const canReject  = me.role === "Admin" && incident.status !== "Closed" && incident.status !== "Rejected";
  const canForceClose = me.role === "Admin" && incident.status !== "Closed" && incident.status !== "Rejected";

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

        <WorkflowOutputs incidentId={incident.id} />

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
          <div className="reply-row" style={{ marginTop: 10 }}>
            <input
              placeholder="Reply or @tag someone…"
              value={msg}
              onChange={e => {
                const v = e.target.value; const at = v.lastIndexOf("@");
                if (at >= 0 && !v.slice(at).includes(" ")) { setTagInput(v.slice(at + 1)); setMsg(v.slice(0, at)); }
                else { setMsg(v); setTagInput(""); }
              }}
              onKeyDown={e => { if (e.key === "Enter") send(); }}
            />
            <button className="primary" onClick={send}>Send</button>
          </div>
          {tagInput && (
            <div style={{ position: "relative" }}>
              <div style={{ position: "absolute", top: 0, left: 0, right: 60, background: "white", border: "0.5px solid var(--border)", borderRadius: "var(--radius)", zIndex: 5 }}>
                {users.filter(u => u.fullName.toLowerCase().includes(tagInput.toLowerCase())).slice(0, 5).map(u => (
                  <div key={u.id} style={{ padding: "4px 8px", cursor: "pointer" }}
                       onClick={() => { setTagged([...tagged, u.id]); setMsg(m => m + " @" + u.firstName + " "); setTagInput(""); }}>
                    {u.fullName} <span style={{ color: "var(--text-soft)", fontSize: 10 }}>· {u.role}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
          {tagged.length > 0 && (
            <p style={{ fontSize: 11, color: "var(--text-soft)", marginTop: 4 }}>
              Tagging: {tagged.map(id => users.find(u => u.id === id)?.fullName).join(", ")}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
