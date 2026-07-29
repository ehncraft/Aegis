// Global system-Admin override, via entity-hierarchy `in`.
permit(principal in Role::"system-admin", action == Action::"reviewPlatformChangeRequest", resource);
