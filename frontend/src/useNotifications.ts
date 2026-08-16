// SignalR hook: connects to /hubs/notifications with the JWT in the query
// string (handled in backend Program.cs JwtBearerEvents). On each "notification"
// event we prepend to a local list; the bell icon shows the unread count.

import { useEffect, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { api, tokenStore, type Notification } from "./api";

export function useNotifications() {
  const [items, setItems] = useState<Notification[]>([]);
  const [unread, setUnread] = useState(0);

  const refresh = useCallback(async () => {
    try {
      const list = await api.notifications(false);
      setItems(list);
      setUnread(list.filter(n => !n.readAt).length);
    } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    if (!tokenStore.get()) return;
    refresh();
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/notifications?access_token=${tokenStore.get()}`)
      .withAutomaticReconnect()
      .build();
    conn.on("notification", () => { refresh(); });
    conn.start().catch(() => { /* will retry on reconnect */ });
    return () => { conn.stop(); };
  }, [refresh]);

  return { items, unread, refresh, markAllRead: async () => { await api.markAllRead(); await refresh(); } };
}
