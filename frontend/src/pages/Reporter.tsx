import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Topbar } from "./Topbar";
import { api, type Incident } from "../api";

// Reporter screen: mobile-first, minimal. "My tickets" + "Report new" + cards.
export function Reporter() {
  const [items, setItems] = useState<Incident[]>([]);
  const [categories, setCategories] = useState<{ id: number; name: string }[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [categoryId, setCategoryId] = useState<number>(0);
  const [description, setDescription] = useState("");
  const [busy, setBusy] = useState(false);
  const nav = useNavigate();

  async function load() { setItems(await api.myIncidents()); }
  useEffect(() => { load(); api.categories().then(c => { setCategories(c); if (c.length && !categoryId) setCategoryId(c[0].id); }); }, []);

  async function submit() {
    if (!description.trim()) return;
    setBusy(true);
    try { await api.createIncident(categoryId, description); setDescription(""); setShowForm(false); await load(); }
    finally { setBusy(false); }
  }

  return (
    <div className="app">
      <Topbar title="My tickets" />
      <div className="container reporter-shell">
        <div className="reporter-card">
          <button className="primary" style={{ width: "100%", padding: 10, marginBottom: 14 }}
                  onClick={() => setShowForm(s => !s)}>
            + Report new incident
          </button>
          {showForm && (
            <div className="card" style={{ marginBottom: 14 }}>
              <label>Category</label>
              <select value={categoryId} onChange={e => setCategoryId(Number(e.target.value))}>
                {categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
              <label style={{ marginTop: 8 }}>Description</label>
              <textarea rows={3} value={description} onChange={e => setDescription(e.target.value)} />
              <div style={{ display: "flex", gap: 8, marginTop: 8 }}>
                <button onClick={() => setShowForm(false)}>Cancel</button>
                <button className="primary" onClick={submit} disabled={busy || !description.trim()}>Submit</button>
              </div>
            </div>
          )}
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {items.length === 0 && <p style={{ color: "var(--text-soft)", textAlign: "center" }}>No tickets yet.</p>}
            {items.map(t => (
              <div key={t.id} className="ticket-card" onClick={() => nav(`/incident/${t.id}`)}>
                <div className="row">
                  <span style={{ fontSize: 13, fontWeight: 500 }}>{t.ticketRef}</span>
                  <span className={`pill ${pillClass(t.status)}`}>{pillLabel(t.status)}</span>
                </div>
                <div className="meta">{t.category.name} · {relTime(t.createdAt)}</div>
                {t.status === "Resolved" && (
                  <div className="ticket-actions" onClick={e => e.stopPropagation()}>
                    <button onClick={async () => { await api.confirm(t.id); await load(); }}>Confirm fixed</button>
                    <button className="danger" onClick={async () => { await api.reopen(t.id); await load(); }}>Reopen</button>
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

export function pillClass(s: string) {
  if (s === "Reopened") return "InProgress";
  return s;
}
export function pillLabel(s: string) {
  if (s === "InProgress") return "In progress";
  if (s === "Reopened") return "Reopened";
  return s;
}
function relTime(iso: string) {
  const d = new Date(iso); const ms = Date.now() - d.getTime();
  const m = Math.floor(ms / 60000); if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60); if (h < 24) return `${h}h ago`;
  const day = Math.floor(h / 24); return `${day}d ago`;
}
