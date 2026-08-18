import { useEffect, useState } from "react";
import { Topbar } from "./Topbar";
import { api, type Incident, type User } from "../api";
import { useNavigate } from "react-router-dom";
import { WorkflowOutputs } from "../components/WorkflowOutputs";
import { MentionInput } from "../components/MentionInput";

// Resolver dashboard: 3 panels (unassigned pool | my tickets | conversation thread).
// Selecting a ticket in either left panel loads the thread on the right.

export function Resolver() {
  const [pool, setPool] = useState<Incident[]>([]);
  const [mine, setMine] = useState<Incident[]>([]);
  const [selected, setSelected] = useState<Incident | null>(null);
  const [users, setUsers] = useState<User[]>([]);
  const me = JSON.parse(localStorage.getItem("im_user")!);
  const nav = useNavigate();

  async function load() {
    const [p, m] = await Promise.all([api.pool(), api.mine()]);
    setPool(p); setMine(m);
    // Auto-select the first "my" ticket so the right panel isn't empty
    if (!selected && m.length) setSelected(m[0]);
    return m;
  }
  useEffect(() => { load(); api.users().then(setUsers); }, []);

  async function pick(id: string) {
    await api.selfPick(id);
    const m = await load();
    setSelected(m.find(x => x.id === id) ?? null);
  }
  async function resolve(id: string) {
    await api.resolve(id);
    await load();
  }

  return (
    <div className="app">
      <Topbar title="Resolver dashboard" />
      <div className="container">
        <div className="resolver-grid">
          <div>
            <p className="panel-title">Unassigned pool ({pool.length})</p>
            {pool.length === 0 && <p style={{ color: "var(--text-soft)", fontSize: 12 }}>No tickets waiting.</p>}
            {pool.map(t => (
              <div key={t.id} className="pool-card">
                <div style={{ fontSize: 12, fontWeight: 500 }}>{t.ticketRef}</div>
                <div style={{ fontSize: 11, color: "var(--text-soft)", margin: "2px 0 6px" }}>{t.category.name}</div>
                <button onClick={() => pick(t.id)}>Self-pick</button>
              </div>
            ))}
          </div>
          <div>
            <p className="panel-title">My tickets ({mine.length})</p>
            {mine.length === 0 && <p style={{ color: "var(--text-soft)", fontSize: 12 }}>Pick a ticket from the pool to get started.</p>}
            {mine.map(t => (
              <div key={t.id} className={`my-card ${selected?.id === t.id ? "selected" : ""}`}
                   onClick={() => setSelected(t)}>
                <div style={{ fontSize: 12, fontWeight: 500 }}>{t.ticketRef}</div>
                <div style={{ fontSize: 11, color: "var(--text-soft)" }}>{t.category.name}</div>
              </div>
            ))}
          </div>
          <div className="card" style={{ background: "var(--bg)" }}>
            {selected ? (
              <>
                <p style={{ fontSize: 12, fontWeight: 500, margin: "0 0 8px" }}>{selected.ticketRef} conversation</p>
                <Thread incidentId={selected.id} />
                {(selected.currentAssigneeId === me.id || me.role === "Admin")
                  && (selected.status === "InProgress" || selected.status === "Reopened") && (
                  <div style={{ display: "flex", gap: 6, marginTop: 10 }}>
                    <button className="primary" onClick={() => resolve(selected.id)}>✓ Mark resolved</button>
                  </div>
                )}
              </>
            ) : (
              <p style={{ color: "var(--text-soft)", fontSize: 12 }}>Select a ticket to view the conversation.</p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function Thread({ incidentId }: { incidentId: string }) {
  const [incident, setIncident] = useState<Incident | null>(null);
  const [msg, setMsg] = useState("");
  const [tagged, setTagged] = useState<string[]>([]);
  const [users, setUsers] = useState<User[]>([]);
  const me = JSON.parse(localStorage.getItem("im_user")!);

  async function load() { setIncident(await api.incident(incidentId)); }
  useEffect(() => { load(); api.users().then(setUsers); }, [incidentId]);

  if (!incident) return <p style={{ color: "var(--text-soft)" }}>Loading…</p>;

  async function send() {
    if (!msg.trim()) return;
    await api.addComment(incidentId, msg, tagged);
    setMsg(""); setTagged([]);
    await load();
  }

  return (
    <>
      <div className="thread" style={{ maxHeight: 360, overflow: "auto" }}>
        {incident.comments?.map(c => {
          const isMine = c.authorId === me.id;
          return (
            <div key={c.id} className={`bubble ${isMine ? "out" : "in"}`}>
              <div style={{ fontSize: 10, color: "var(--text-soft)" }}>{isMine ? "You" : c.author?.fullName ?? "User"}</div>
              {c.message}
              {c.taggedUserIds && <div style={{ fontSize: 10, color: "var(--teal)" }}>@mentioned: {taggedNames(c.taggedUserIds, users)}</div>}
            </div>
          );
        })}
        {incident.comments?.length === 0 && <p style={{ color: "var(--text-soft)", fontSize: 12 }}>No messages yet.</p>}
      </div>
      <MentionInput
        users={users} meId={me.id}
        value={msg} onChange={setMsg} onSend={send}
        tagged={tagged} onTaggedChange={setTagged}
        placeholder="Reply or @tag someone…"
      />
      <WorkflowOutputs incidentId={incidentId} role={me.role} />
    </>
  );
}

function taggedNames(ids: string | string[], users: User[]): string {
  const arr = Array.isArray(ids) ? ids : (ids ? ids.split(";").filter(Boolean) : []);
  return arr.map(id => users.find(u => u.id === id)?.fullName ?? "?").join(", ") || "—";
}
