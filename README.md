# Incident Management App

A structured, permissioned ticketing system that replaces free-text WhatsApp
incident reporting. Three roles (Reporter, Resolver, Admin) backed by a
.NET Core 8 API, a React PWA, and a Python reporting job.

## Layout

```
src/
  backend/    .NET Core 8 Web API + EF Core (SQLite for dev, MS SQL for prod)
  frontend/   React + Vite PWA (installable, mobile-first)
  reporting/  Python job: reads DB, generates Excel + PowerPoint
```

## Quick start (dev, SQLite, no external services)

### 1. Install missing toolchain
- **.NET 8 SDK** (only the runtime is currently installed; the SDK is needed
  to compile and run the API). Get it: <https://dot.net/download>
- Node 18+ and npm (already present).
- Python 3.10+ (already present).

### 2. Run the API

```bash
cd src/backend
dotnet restore
dotnet run
```

- Listens on <http://localhost:5080>
- Swagger UI: <http://localhost:5080/swagger>
- SQLite DB file is created at `src/backend/incident_management.db` on first run
  and auto-migrated + seeded with 9 categories and 12 test users.

### 3. Run the frontend

```bash
cd src/frontend
npm install
npm run dev
```

- Open <http://localhost:5173>
- The Vite dev server proxies `/api` and `/hubs` to the backend.

### 4. Log in

Any seeded mobile number with OTP `123456` works:

| Role     | Mobile             | Notes                       |
|----------|--------------------|-----------------------------|
| Reporter | `+91 98220 11234`  | Has 1 sample ticket         |
| Resolver | `+91 90000 00001`  | Darshan Patil               |
| Resolver | `+91 90000 00002`  | Vamshi                      |
| Resolver | `+91 90000 00003`  | Ganesh Gupta                |
| Resolver | `+91 90000 00004`  | Shivam Singh                |
| Resolver | `+91 90000 00005`  | Sumit Kumar                 |
| Resolver | `+91 90000 00006`  | Ravindra Patwa              |
| Admin    | `+91 90000 00099`  | Nilesh Gaidhani             |

A new mobile number goes through the reporter self-registration flow and is
asked for First/Last/Email on first login.

## Switch to MS SQL (production)

1. Install MS SQL Server (Express is free).
2. Set `Database__Provider=SqlServer` and `ConnectionStrings__SqlServer=...`
   in the environment, OR flip `Database:Provider` in
   `appsettings.Production.json` and provide the connection string.
3. Run `dotnet ef database update` against MS SQL (migration files are
   provider-agnostic — they apply to both).

## Reporting job

```bash
cd src/reporting
python -m pip install -r requirements.txt
python generate_reports.py
```

The script reads from the same DB the API uses (SQLite for dev, MS SQL for
prod — switch via `DB_PROVIDER` / `DB_CONNECTION_STRING` env vars), writes
`Admin_Report_YYYYMMDD_HHMMSS.xlsx` and `.pptx` into `output/`, and the
Admin UI's "Generate report" button calls it as a subprocess and serves
the files back.

## Architecture

- **Auth**: Phone + OTP, JWT bearer token. `IOtpSender` abstraction
  (`backend/Infrastructure/Auth/IOtpSender.cs`); `TestOtpSender` always
  returns `123456` for dev. To go live, implement `IOtpSender` against
  MSG91 / Twilio / Kaleyra / Gupshup and switch the DI registration in
  `Program.cs`. Pick a vendor that covers BOTH SMS and WhatsApp Business
  API so the future WhatsApp-template notifications don't need a second
  integration.
- **Real-time**: SignalR hub at `/hubs/notifications`. JWT comes in via
  `Authorization` header or `?access_token=...` for WebSocket. Frontend
  connects via the `useNotifications` hook; the bell badge in the topbar
  lights up on each `notification` event.
- **State machine**: All transitions in `Services/IncidentService.cs`.
  Reporter, Resolver, Admin actions are enforced at the service level,
  with role checks on the controller as defense in depth.
- **Data model**: 11 tables — `Users`, `Categories`, `Incidents`,
  `IncidentAssignments`, `Comments`, `Notifications`, plus the workflow
  tables `Workflows`, `WorkflowSteps`, `WorkflowInputs`, `WorkflowRuns`,
  `WorkflowStepResults`. `IncidentAssignments`
  is append-only so reassignment history is preserved; the per-ticket
  `current_assignee_id` is just a cache of the latest row.
- **Workflow builder**: Admins/Resolvers compose named chains of API steps
  (`/workflows` page → `GET/POST/PUT/PATCH/DELETE` calls) with declared
  inputs and per-step auth (Bearer / Basic / ApiKey). Steps run
  server-to-server sequentially; auth configs are encrypted at rest with
  AES-256-CBC (`Infrastructure/Auth/AuthConfigProtector.cs`, key from
  `Workflow:AuthConfigEncryptionKey`). URL/header/body templates support
  `{{input.fieldName}}` and `{{stepN.response.path}}` placeholders. A run
  can be attached to an incident; the reporter is notified and the rendered
  output appears inline in the ticket thread.
- **Universal output rule**: no role — including Admin — ever sees a raw
  step response. `Services/WorkflowOutputRenderer.cs` converts every
  response to a table (object → Field/Value, array → multi-column, scalar →
  single Value column); `frontend/src/components/JsonTable.tsx` renders it.
  Raw payloads are kept only for the run audit trail.
- **Database provider is swappable.** Same domain model compiles for
  SQLite (dev) and MS SQL (prod). The `ProviderFactory` reads
  `Database:Provider` from config and routes to the right EF provider.
  No SQLite-specific types anywhere in the domain.
- **Notifications schema is channel-agnostic.** Phase 1 (in-app via
  SignalR) is built. Phase 2 (web push) adds a `Channel` column or a
  separate `OutboundNotifications` table. Phase 3 (WhatsApp Business
  templates) plugs into the same `INotificationDispatcher` interface.

## Testing the API manually

A REST Client file is at `src/backend/Incidents.http` (works with the VS Code
"REST Client" extension). It walks through request-otp → verify-otp →
listing incidents → status counts → creating a ticket as a reporter.

## Known follow-ups (intentional, documented)

These are known gaps from the MVP build. None block a pilot; each is a
small, well-scoped change when you want to address it.

1. **@mention autocomplete UX is rough.** The current input lets you
   type `@` then a name; the suggestion list is positioned absolutely
   but doesn't dismiss on outside click, and tag chips aren't shown
   inline. Functional, not delightful.
   - *Fix:* lift tag state out of the raw input, show selected tags as
     removable chips above the input, and add a `useEffect` for
     outside-click-to-dismiss. About 30 lines in
     `frontend/src/pages/Resolver.tsx` and `IncidentDetail.tsx`.

2. **Real OTP provider + production hardening.** Dev uses
   `Auth:UseTestOtp=true` (fixed code `123456`). Swap in a real
   `IOtpSender` (MSG91 / Twilio / Kaleyra / Gupshup), set a strong
   `Jwt:Key`, and point `Database:Provider=SqlServer` at the real
   connection string before going live. The `NotImplementedException`
   in `Program.cs` guards the non-test path deliberately.

3. **Web push (Phase 2) / WhatsApp Business API (Phase 3).** The
   notifications schema and `INotificationDispatcher` are ready; the
   SignalR delivery is the only implementation today.

## Change log

- **Workflow builder (PRD §6a)**: `WorkflowsController` — definition CRUD
  (`/api/workflows`), runner (`POST /{id}/run`), run history + detail
  (`GET /runs`, `GET /runs/{runId}`), and `GET /api/incidents/{id}/workflow-outputs`
  for the inline thread view. Frontend: `/workflows` page (builder modal with
  inputs/steps/auth + run-history tab with rendered tables), Topbar nav link
  for Resolver/Admin, and rendered outputs in the incident thread. Requires
  `Workflow:AuthConfigEncryptionKey` in config (set in `appsettings.json`).
- **Rejection audit**: `Incident.RejectedById`/`RejectedAt` added (EF
  migration `AddRejectedBy`). The rejection log report now shows the
  rejecting admin's name and time instead of an "Admin" placeholder.
- **Auto-close**: a hosted background service closes `Resolved` tickets
  the reporter never confirmed after `Incident:AutoCloseAfterHours`
  (default 48h), sweeping every `Incident:AutoCloseCheckMinutes` (10).
- **Admin force-close**: `POST /api/incidents/{id}/force-close` closes
  any ticket not already Closed/Rejected; exposed as a button on the
  incident detail view.
- **Admin can resolve**: `MarkResolvedAsync` now accepts an admin even
  when they aren't the assignee (spec grants Admin mark-resolved).
- **Comment access control**: commenting now enforces role visibility —
  Reporter only on own tickets, Resolver on assigned/@tagged tickets,
  Admin on anything until Closed.
- **Admin console**: reassign action for assigned tickets, plus a user
  management section (add resolver/admin, disable accounts).
- **PWA icons**: `public/icon-192.png` and `icon-512.png` shipped so the
  manifest is valid and the app is installable.
