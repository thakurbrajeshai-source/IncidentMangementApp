import { useState, useRef, useEffect } from "react";
import type { User } from "../api";

// Shared @mention input with outside-click dismiss and inline removable chips.
export function MentionInput({
  users, meId, value, onChange, onSend, tagged, onTaggedChange, placeholder,
}: {
  users: User[];
  meId: string;
  value: string;
  onChange: (v: string) => void;
  onSend: () => void;
  tagged: string[];
  onTaggedChange: (ids: string[]) => void;
  placeholder?: string;
}) {
  const [tagInput, setTagInput] = useState("");
  const [showSuggestions, setShowSuggestions] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Outside-click dismiss
  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) {
        setShowSuggestions(false);
      }
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, []);

  function handleChange(v: string) {
    const at = v.lastIndexOf("@");
    if (at >= 0 && !v.slice(at).includes(" ")) {
      setTagInput(v.slice(at + 1));
      setShowSuggestions(true);
      onChange(v.slice(0, at));
      return;
    }
    setTagInput("");
    setShowSuggestions(false);
    onChange(v);
  }

  function addTag(user: User) {
    if (!tagged.includes(user.id) && user.id !== meId) {
      onTaggedChange([...tagged, user.id]);
    }
    setTagInput("");
    setShowSuggestions(false);
    onChange(value + " @" + user.firstName + " ");
    inputRef.current?.focus();
  }

  function removeTag(id: string) {
    onTaggedChange(tagged.filter(t => t !== id));
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      onSend();
    }
  }

  const suggestions = showSuggestions && tagInput
    ? users.filter(u =>
        u.id !== meId &&
        (u.fullName.toLowerCase().includes(tagInput.toLowerCase()) ||
         u.firstName.toLowerCase().includes(tagInput.toLowerCase()))
      ).slice(0, 5)
    : [];

  return (
    <div ref={wrapRef} style={{ position: "relative" }}>
      {tagged.length > 0 && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 4, marginBottom: 6 }}>
          {tagged.map(id => {
            const u = users.find(x => x.id === id);
            return (
              <span key={id} style={{
                display: "inline-flex", alignItems: "center", gap: 4,
                padding: "2px 8px", borderRadius: 999, fontSize: 11,
                background: "#f0fdfa", border: "1px solid var(--teal)", color: "var(--teal)",
              }}>
                @{u?.firstName ?? "User"}
                <button onClick={() => removeTag(id)}
                  style={{ background: "none", border: "none", cursor: "pointer", padding: 0, fontSize: 13, lineHeight: 1, color: "var(--teal)" }}>
                  ×
                </button>
              </span>
            );
          })}
        </div>
      )}
      <div className="reply-row">
        <input
          ref={inputRef}
          placeholder={placeholder || "Reply or @tag someone…"}
          value={value}
          onChange={e => handleChange(e.target.value)}
          onKeyDown={handleKeyDown}
        />
        <button className="primary" onClick={onSend}>Send</button>
      </div>
      {suggestions.length > 0 && (
        <div style={{
          position: "absolute", bottom: "100%", left: 0, right: 60,
          background: "white", border: "0.5px solid var(--border)",
          borderRadius: "var(--radius)", zIndex: 50, marginBottom: 4,
          boxShadow: "0 2px 8px rgba(0,0,0,0.1)",
        }}>
          {suggestions.map(u => (
            <div key={u.id}
              style={{ padding: "6px 10px", cursor: "pointer", fontSize: 12 }}
              onClick={() => addTag(u)}
              onMouseEnter={e => (e.currentTarget.style.background = "var(--muted-bg)")}
              onMouseLeave={e => (e.currentTarget.style.background = "white")}>
              <b>{u.fullName}</b> <span style={{ color: "var(--text-soft)", fontSize: 10 }}>· {u.role}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
