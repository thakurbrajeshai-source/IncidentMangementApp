namespace IncidentManagement.Api.Domain;

public enum UserRole { Reporter = 1, Resolver = 2, Admin = 3 }
public enum UserStatus { Active = 1, Disabled = 2 }

// Matches the state machine in PRD section 2 / design deck slide 4.
// Order matters: lifecycle progresses left to right, with Rejected and Reopened as side branches.
public enum IncidentStatus
{
    Open = 1,
    InProgress = 2,
    Resolved = 3,
    Closed = 4,
    Rejected = 5,
    Reopened = 6, // transient marker; UI typically renders as "In Progress" with a "revert_count > 0" badge
}

public enum AssignmentType
{
    SelfPicked = 1,
    AdminAssigned = 2,
    Tagged = 3,        // mentioned in a comment; becomes a participant but not the primary owner
    Reassigned = 4,   // explicit reassignment by admin OR by resolver (post-reopen), tracks history
}

public enum NotificationType
{
    TicketCreated = 1,        // to: resolvers + admin (new ticket in pool)
    TicketAssigned = 2,       // to: resolver (assigned or self-picked)
    TicketTagged = 3,         // to: tagged user
    TicketResolved = 4,       // to: reporter (please confirm)
    TicketClosed = 5,         // to: resolver (reporter confirmed)
    TicketReopened = 6,       // to: resolver (reporter says it's not fixed)
    TicketRejected = 7,       // to: reporter (admin rejected with reason)
    NewComment = 8,           // to: other participants in the thread
    WorkflowRunComplete = 9,  // to: reporter (a workflow ran on their ticket), plus the runner
}

// Workflow builder (PRD section 6a) — run lifecycle.
public enum WorkflowRunStatus { Running = 1, Success = 2, Failed = 3 }

/// <summary>Auth options for a workflow step's HTTP call. The config payload is
/// stored encrypted at rest (WorkflowStep.AuthConfigEncrypted).</summary>
public enum WorkflowAuthType { None = 1, Bearer = 2, Basic = 3, ApiKey = 4 }
