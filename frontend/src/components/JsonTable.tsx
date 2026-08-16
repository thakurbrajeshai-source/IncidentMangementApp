import type { RenderedTable } from "../api";

// Renders a backend-produced RenderedTable ({ columns, rows }) as an HTML table.
// This is the ONLY place workflow step output is shown — raw JSON never reaches the UI.
export function JsonTable({ table }: { table: RenderedTable }) {
  if (!table || !table.columns || table.columns.length === 0) return null;
  return (
    <div className="json-table-wrap">
      <table className="json-table">
        <thead>
          <tr>{table.columns.map(c => <th key={c}>{c}</th>)}</tr>
        </thead>
        <tbody>
          {table.rows.map((r, i) => (
            <tr key={i}>
              {table.columns.map(c => (
                <td key={c}>{cell(r[c])}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function cell(v: unknown): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "object") return JSON.stringify(v);
  return String(v);
}
