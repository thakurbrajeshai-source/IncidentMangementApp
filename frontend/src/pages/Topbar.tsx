import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { tokenStore } from "../api";
import { useNotifications } from "../useNotifications";

// Topbar with notification bell + dropdown. Used by all role pages.
export function Topbar({ title }: { title: string }) {
  const u = tokenStore.user();
  const nav = useNavigate();
  const { items, unread, markAllRead } = useNotifications();
  const [open, setOpen] = useState(false);

  return (
    <div className="topbar">
      <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
        <div className="title">{title}</div>
        {u && u.role !== "Reporter" && (
          <button className="ghost nav-link" onClick={() => nav("/workflows")}>⚙ Workflows</button>
        )}
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        <span className="user">{u?.fullName} · {u?.role}</span>
        <div className="bell" onClick={() => { setOpen(o => !o); if (!open) markAllRead(); }}>
          🔔 {unread > 0 && <span className="badge">{unread}</span>}
        </div>
        <button className="ghost" onClick={() => { tokenStore.clear(); nav("/login"); }}>Logout</button>
      </div>
      {open && (
        <div className="notif-list" onClick={e => e.stopPropagation()}>
          {items.length === 0 && <div className="notif-item"><div className="body">No notifications yet.</div></div>}
          {items.map(n => (
            <div key={n.id} className={`notif-item ${!n.readAt ? "unread" : ""}`}
                 onClick={() => { setOpen(false); if (n.incidentId) nav(`/incident/${n.incidentId}`); }}>
              <div className="title">{n.title}</div>
              <div className="body">{n.body}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
