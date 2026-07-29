// Department-scoped approval, gated on a permission passed via context.
permit(principal, action == Action::"approveLeaveRequest", resource)
when {
    principal.departmentId == resource.departmentId &&
    context.permissions.contains("approve_leave")
};

// Explicit deny always wins.
forbid(principal, action == Action::"approveLeaveRequest", resource)
when { principal.status == "suspended" };
