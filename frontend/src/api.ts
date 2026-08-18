// Tiny fetch wrapper. Stores JWT in localStorage; redirects to /login on 401.

const TOKEN_KEY = "im_token";
const USER_KEY = "im_user";

export type Role = "Reporter" | "Resolver" | "Admin";
export type UserStatus = "Active" | "Disabled";

export interface User {
  id: string;
  firstName: string;
  lastName: string;
  fullName: string;
  mobile: string;
  email?: string | null;
  role: Role;
  status?: UserStatus;
}

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t: string) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => { localStorage.removeItem(TOKEN_KEY); localStorage.removeItem(USER_KEY); },
  user: (): User | null => {
    const s = localStorage.getItem(USER_KEY);
    return s ? JSON.parse(s) : null;
  },
  setUser: (u: User) => localStorage.setItem(USER_KEY, JSON.stringify(u)),
};

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(tokenStore.get() ? { Authorization: `Bearer ${tokenStore.get()}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (res.status === 401) { tokenStore.clear(); if (location.pathname !== "/login") location.href = "/login"; throw new Error("Unauthorized"); }
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try { const j = await res.json(); if (j?.error) msg = j.error; } catch {}
    throw new Error(msg);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

export const api = {
  // Auth
  requestOtp: (mobile: string) => call<{ mobile: string; devOtp: string | null }>("POST", "/auth/request-otp", { mobile }),
  verifyOtp: (mobile: string, otp: string, reg?: { firstName: string; lastName: string; email: string }) =>
    call<{ accessToken: string; isNewUser: boolean; user: User }>("POST", "/auth/verify-otp", { mobile, otp, ...(reg ?? {}) }),

  // Data
  categories: () => call<{ id: number; name: string }[]>("GET", "/categories"),
  myIncidents: () => call<Incident[]>("GET", "/incidents"),
  pool: () => call<Incident[]>("GET", "/incidents?scope=pool"),
  mine: () => call<Incident[]>("GET", "/incidents?scope=mine"),
  allIncidents: () => call<Incident[]>("GET", "/incidents"),
  incident: (id: string) => call<Incident>(`GET`, `/incidents/${id}`),
  statusCounts: () => call<StatusCounts>("GET", "/incidents/status-counts"),
  users: (role?: Role) => call<User[]>("GET", `/users${role ? `?role=${role}` : ""}`),

  // Mutations
  createIncident: (categoryId: number, description: string) =>
    call<Incident>("POST", "/incidents", { categoryId, description }),
  selfPick: (id: string) => call("POST", `/incidents/${id}/self-pick`),
  resolve: (id: string) => call("POST", `/incidents/${id}/resolve`),
  confirm: (id: string) => call("POST", `/incidents/${id}/confirm`),
  reopen: (id: string) => call("POST", `/incidents/${id}/reopen`),
  assign: (id: string, resolverId: string) => call("POST", `/incidents/${id}/assign`, { resolverId }),
  reject: (id: string, reason: string) => call("POST", `/incidents/${id}/reject`, { reason }),
  reassign: (id: string, resolverId: string) => call("POST", `/incidents/${id}/reassign`, { resolverId }),
  forceClose: (id: string) => call("POST", `/incidents/${id}/force-close`),

  // Admin: user management
  createUser: (mobile: string, firstName: string, lastName: string, email: string, role: Role) =>
    call("POST", "/users", { mobile, firstName, lastName, email, role }),
  disableUser: (id: string) => call("POST", `/users/${id}/disable`),

  addComment: (incidentId: string, message: string, taggedUserIds: string[] = []) =>
    call("POST", `/incidents/${incidentId}/comments`, { message, taggedUserIds }),

  // Notifications
  notifications: (unreadOnly = false) => call<Notification[]>("GET", `/notifications${unreadOnly ? "?unreadOnly=true" : ""}`),
  markAllRead: () => call<{ updated: number }>("POST", "/notifications/mark-all-read"),

  // Reports
  generateReport: () => call<{ excelUrl: string; pptUrl: string; stdout: string; exitCode: number }>("POST", "/reports/generate"),
  latestReports: () => call<{ name: string; url: string; sizeBytes: number; generatedAt: string }[]>("GET", "/reports/latest"),

  // Workflows (builder + runner, Resolver/Admin)
  workflows: () => call<Workflow[]>("GET", "/workflows"),
  workflow: (id: string) => call<Workflow>("GET", `/workflows/${id}`),
  createWorkflow: (w: WorkflowSave) => call<{ id: string }>("POST", "/workflows", w),
  updateWorkflow: (id: string, w: WorkflowSave) => call("PUT", `/workflows/${id}`, w),
  deleteWorkflow: (id: string) => call("DELETE", `/workflows/${id}`),
  runWorkflow: (id: string, incidentId: string | null, inputs: Record<string, string>) =>
    call<{ runId: string; status: string }>("POST", `/workflows/${id}/run`, { incidentId, inputs }),
  workflowRuns: () => call<RunSummary[]>("GET", "/workflows/runs"),
  workflowRunDetail: (runId: string) => call<RunDetail>("GET", `/workflows/runs/${runId}`),
  incidentWorkflowOutputs: (incidentId: string) => call<IncidentRunOutput[]>("GET", `/incidents/${incidentId}/workflow-outputs`),

  // Workflow-category assignment
  setWorkflowCategories: (id: string, categoryIds: number[]) =>
    call("PUT", `/workflows/${id}/categories`, { categoryIds }),

  // Attach workflow to incident (Resolver/Admin)
  attachWorkflow: (incidentId: string, workflowId: string, inputs: Record<string, string> = {}) =>
    call<{ runId: string; status: string }>("POST", "/workflows/attach", { incidentId, workflowId, inputs }),

  // Toggle workflow visibility in comments
  setWorkflowVisibility: (incidentId: string, workflowId: string, visible: boolean) =>
    call("PUT", "/workflows/visibility", { incidentId, workflowId, visible }),

  // Available workflows to attach
  availableWorkflows: () => call<AvailableWorkflow[]>("GET", "/workflows/available"),

  // Reporter: run workflow on own ticket
  runWorkflowOnTicket: (incidentId: string, workflowId: string, inputs: Record<string, string> = {}) =>
    call<{ runId: string; status: string }>("POST", `/incidents/${incidentId}/run-workflow`, { workflowId, inputs }),

  // Per-user-per-ticket run count
  runCount: (incidentId: string) => call<RunCountResponse>("GET", `/incidents/${incidentId}/run-count`),

  // Default workflow for a category
  defaultWorkflow: (categoryId: number) => call<DefaultWorkflow | null>("GET", `/incidents/default-workflow/${categoryId}`),
};

export type IncidentStatus = "Open" | "InProgress" | "Resolved" | "Closed" | "Rejected" | "Reopened";

export interface Comment {
  id: string;
  incidentId: string;
  authorId: string;
  author: User | null;
  message: string;
  taggedUserIds: string;
  createdAt: string;
}

export interface Incident {
  id: string;
  ticketRef: string;
  reporterId: string;
  reporter: User | null;
  categoryId: number;
  category: { id: number; name: string };
  description: string;
  status: IncidentStatus;
  currentAssigneeId: string | null;
  currentAssignee: User | null;
  rejectionReason: string | null;
  createdAt: string;
  resolvedAt: string | null;
  closedAt: string | null;
  revertCount: number;
  comments: Comment[];
  attachedWorkflows?: AttachedWorkflow[];
}

export interface Notification {
  id: string;
  type: string;
  title: string;
  body: string;
  incidentId: string | null;
  createdAt: string;
  readAt: string | null;
}

export interface StatusCounts { open: number; inProgress: number; closed: number; reverted: number; }

// Workflows
export type WorkflowAuthType = "None" | "Bearer" | "Basic" | "ApiKey";
export type WorkflowRunStatus = "Running" | "Success" | "Failed";

export interface WorkflowStep {
  id?: string | null;
  stepOrder: number;
  name: string;
  httpMethod: string;
  urlTemplate: string;
  headers: Record<string, string>;
  bodyTemplate: string;
  authType: WorkflowAuthType;
  authConfig: Record<string, string>;
}

export interface WorkflowInput {
  id?: string | null;
  fieldName: string;
  label: string;
  type: string;
  required: boolean;
}

export interface Workflow {
  id: string;
  name: string;
  description: string;
  isActive: boolean;
  createdAt: string;
  createdById: string;
  createdByFullName: string;
  stepCount?: number;
  inputCount?: number;
  inputs?: WorkflowInput[];
  steps?: WorkflowStep[];
  categories?: { id: number; name: string }[];
}

export interface WorkflowSave {
  name: string;
  description: string;
  isActive: boolean;
  inputs: WorkflowInput[];
  steps: WorkflowStep[];
}

/** Rendered output table (never raw JSON — see WorkflowOutputRenderer). */
export interface RenderedTable {
  columns: string[];
  rows: Record<string, unknown>[];
}

export interface RunStepOutput {
  stepOrder: number;
  stepName: string;
  statusCode: number | null;
  succeeded: boolean;
  errorMessage: string | null;
  executedAt: string;
  table: RenderedTable;
}

export interface RunSummary {
  id: string;
  workflowId: string;
  workflowName: string;
  status: WorkflowRunStatus;
  startedAt: string;
  completedAt: string | null;
  incidentId: string | null;
  incidentTicketRef: string | null;
  triggeredByFullName: string;
  failedStepOrder: number | null;
  errorMessage: string | null;
}

export interface RunDetail extends RunSummary {
  steps: RunStepOutput[];
}

export interface IncidentRunOutput {
  runId: string;
  workflowName: string;
  status: WorkflowRunStatus;
  startedAt: string;
  triggeredByFullName: string;
  visibleInComments: boolean;
  steps: RunStepOutput[];
}

export interface AttachedWorkflow {
  workflowId: string;
  workflowName: string;
  visibleInComments: boolean;
  attachedById: string;
  attachedByFullName: string;
  attachedAt: string;
}

export interface DefaultWorkflow {
  id: string;
  name: string;
  description: string;
  isActive: boolean;
  inputs: WorkflowInput[];
}

export interface AvailableWorkflow {
  id: string;
  name: string;
  description: string;
  inputCount: number;
}

export interface RunCountResponse {
  runCount: number;
}
