namespace SupplyForge.Host.Api;

public static class ApiRoutes
{
    /// <summary>NFR-044: versioned route prefix.</summary>
    public const string V1 = "/api/v1";

    public static class Auth
    {
        public const string Login = V1 + "/auth/login";
        public const string Refresh = V1 + "/auth/refresh";
        public const string Users = V1 + "/users";
    }

    public static class Notifications
    {
        public const string Mine = V1 + "/notifications";
        public const string Read = V1 + "/notifications/{id}/read";

        /// <summary>TD-2 manual mark-sent (ADR-008 explicit workflow).</summary>
        public const string MarkSent = V1 + "/notifications/{id}/mark-sent";
    }

    public static class Health
    {
        public const string Liveness = "/health";
    }
}
