import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Topbar } from "./Topbar";
import { api, type Incident, type User, type StatusCounts, type Role } from "../api";
import { pillClass, pillLabel } from "./Reporter";

// Admin console: full incident table, action column depends on status,
// 4 summary stat cards below, Generate report button at top right.
// Plus a user-management section (add resolvers/admins, disable accounts).

export function Admin() {
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [counts, setCounts] = useState<StatusCounts | null>(null);
  const [resolvers, setResolvers] = useState<User[]>([]);
  const [staff, setStaff] = useState<User[]>([]);
  const [rejectFor, setRejectFor] = useState<Incident | null>(null);
  const [assignFor, setAssignFor] = useState<{ incident: Incident; mode: "assign" | "reassign" } | null>(null);
  const [addUser, setAddUser] = useState(false);
  const [busy, setBusy] = useState(false);
  const nav = useNavigate();

  async function load() {
    const [all, c] = await Promise.all([api.allIncidents(), api.statusCounts()]);
    setIncidents(all); setCounts(c);
  }
  async function loadStaff() {
    const [r, a] = await Promise.all([api.users("Resolver"), api.users("Admin")]);
    setResolvers(r);
    setStaff([...a, ...r]);
  }
  useEffect(() => { load(); loadStaff(); }, []);

  async function generate() {
    setBusy(true);
    try {
      const r = await api.generateReport();
      if (r.excelUrl) window.open(r.excelUrl, "_blank");
      if (r.pptUrl)   window.open(r.pptUrl, "_blank");
    } catch (e: any) { alert(e.message); } finally { setBusy(false); }
  }

  return (
    <div className="app">
      <Topbar title="Admin console" />
      <div className="container">
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 14 }}>
          <div />
          <button className="primary" onClick={generate} disabled={busy}>📊 Generate report</button>
        </div>
        <table className="admin-table">
          <thead>
            <tr>
              <th>Ticket</th><th>Category</th><th>Assignee</th><th>Status</th><th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {incidents.map(t => {
              const terminal = t.status === "Closed" || t.status === "Rejected";
              return (
                <tr key={t.id}>
                  <td>{t.ticketRef}</td>
                  <td>{t.category.name}</td>
                  <td style={{ color: t.currentAssignee ? undefined : "var(--text-soft)" }}>
                    {t.currentAssignee?.fullName ?? "Unassigned"}
                  </td>
                  <td><span className={`pill ${pillClass(t.status)}`}>{pillLabel(t.status)}</span></td>
                  <td>
                    {!t.currentAssigneeId && t.status === "Open" && (
                      <>
                        <button onClick={() => setAssignFor({ incident: t, mode: "assign" })}>Assign</button>{" "}
                        <button className="danger" onClick={() => setRejectFor(t)}>Reject</button>
                      </>
                    )}
                    {t.currentAssigneeId && !terminal && (
                      <>
                        <button onClick={() => setAssignFor({ incident: t, mode: "reassign" })}>Reassign</button>{" "}
                        <button onClick={() => nav(`/incident/${t.id}`)}>Join chat</button>
                      </>
                    )}
                    {terminal && (
                      <button onClick={() => nav(`/incident/${t.id}`)}>View</button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        {counts && (
          <div className="stat-grid">
            <Stat label="Open" value={counts.open} />
            <Stat label="In progress" value={counts.inProgress} />
            <Stat label="Closed" value={counts.closed} />
            <Stat label="Reverted" value={counts.reverted} />
          </div>
        )}

        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: 28 }}>
          <p className="panel-title" style={{ margin: 0, fontSize: 13 }}>User management</p>
          <button className="accent" onClick={() => setAddUser(true)}>+ Add resolver/admin</button>
        </div>
        <table className="admin-table" style={{ marginTop: 8 }}>
          <thead>
            <tr>
              <th>Name</th><th>Mobile</th><th>Email</th><th>Role</th><th>Status</th><th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {staff.map(u => (
              <tr key={u.id}>
                <td>{u.fullName}</td>
                <td>{u.mobile}</td>
                <td>{u.email ?? "—"}</td>
                <td>{u.role}</td>
                <td>{u.status}</td>
                <td>
                  {u.status === "Active" && u.role !== "Admin" && (
                    <button className="danger" onClick={async () => { await api.disableUser(u.id); await loadStaff(); }}>
                      Disable
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {staff.length === 0 && <tr><td colSpan={6} style={{ color: "var(--text-soft)" }}>No resolvers or admins yet.</td></tr>}
          </tbody>
        </table>
      </div>

      {assignFor && (
        <Modal title={`${assignFor.mode === "reassign" ? "Reassign" : "Assign"} ${assignFor.incident.ticketRef}`} onClose={() => setAssignFor(null)}>
          <label>Resolver</label>
          <select id="resolver-select" defaultValue={assignFor.incident.currentAssigneeId ?? resolvers[0]?.id ?? ""}>
            {resolvers.map(r => <option key={r.id} value={r.id}>{r.fullName}</option>)}
          </select>
          <div className="actions">
            <button onClick={() => setAssignFor(null)}>Cancel</button>
            <button className="primary" onClick={async () => {
              const sel = (document.getElementById("resolver-select") as HTMLSelectElement).value;
              if (assignFor.mode === "reassign") await api.reassign(assignFor.incident.id, sel);
              else await api.assign(assignFor.incident.id, sel);
              setAssignFor(null); await load();
            }}>{assignFor.mode === "reassign" ? "Reassign" : "Assign"}</button>
          </div>
        </Modal>
      )}

      {rejectFor && (
        <Modal title={`Reject ${rejectFor.ticketRef}`} onClose={() => setRejectFor(null)}>
          <label>Reason (required)</label>
          <textarea id="reject-reason" rows={3} placeholder="e.g. Duplicate ticket, Insufficient detail, Not an ABHI/Axis issue" />
          <div className="actions">
            <button onClick={() => setRejectFor(null)}>Cancel</button>
            <button className="danger" onClick={async () => {
              const reason = (document.getElementById("reject-reason") as HTMLTextAreaElement).value.trim();
              if (!reason) return;
              await api.reject(rejectFor.id, reason);
              setRejectFor(null); await load();
            }}>Reject</button>
          </div>
        </Modal>
      )}

      {addUser && (
        <AddUserModal onClose={() => setAddUser(false)} onDone={async () => { setAddUser(false); await loadStaff(); }} />
      )}
    </div>
  );
}

function AddUserModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const [mobile, setMobile] = useState("");
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Resolver");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const isFormValid = mobile.trim() && firstName.trim() && lastName.trim();

  async function submit() {
    setError(null);
    
    // Client-side validation
    if (!mobile.trim()) { setError("Mobile number is required"); return; }
    if (!firstName.trim()) { setError("First name is required"); return; }
    if (!lastName.trim()) { setError("Last name is required"); return; }
    if (firstName.trim().length < 2) { setError("First name must be at least 2 characters"); return; }
    if (lastName.trim().length < 2) { setError("Last name must be at least 2 characters"); return; }
    
    setBusy(true);
    try {
      await api.createUser(mobile.trim(), firstName.trim(), lastName.trim(), email.trim(), role);
      onDone();
    } catch (e: any) { setError(e.message); } finally { setBusy(false); }
  }

  return (
    <Modal title="Add resolver / admin" onClose={onClose}>
      <label>Mobile *</label>
      <input value={mobile} onChange={e => setMobile(e.target.value)} placeholder="+91 9xxxxxxxxx" disabled={busy} />
      <small style={{ color: "#666" }}>10-digit Indian format (e.g., 9876543210 or +91 9876543210)</small>
      
      <label style={{ marginTop: 8 }}>First name *</label>
      <input value={firstName} onChange={e => setFirstName(e.target.value)} disabled={busy} />
      
      <label style={{ marginTop: 8 }}>Last name *</label>
      <input value={lastName} onChange={e => setLastName(e.target.value)} disabled={busy} />
      
      <label style={{ marginTop: 8 }}>Email</label>
      <input value={email} onChange={e => setEmail(e.target.value)} type="email" disabled={busy} />
      
      <label style={{ marginTop: 8 }}>Role</label>
      <select value={role} onChange={e => setRole(e.target.value as Role)} disabled={busy}>
        <option value="Resolver">Resolver</option>
        <option value="Admin">Admin</option>
      </select>
      {error && <div className="error">{error}</div>}
      <div className="actions">
        <button onClick={onClose} disabled={busy}>Cancel</button>
        <button className="primary" onClick={submit} disabled={busy || !isFormValid}>Add</button>
      </div>
    </Modal>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return <div className="stat-card"><p className="label">{label}</p><p className="value">{value}</p></div>;
}
function Modal({ title, children, onClose }: { title: string; children: React.ReactNode; onClose: () => void }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <h3>{title}</h3>
        {children}
      </div>
    </div>
  );
}
