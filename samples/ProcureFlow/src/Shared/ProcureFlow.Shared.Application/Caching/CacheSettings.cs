namespace ProcureFlow.Shared.Application.Caching;

public static class CacheSettings
{
    public static class User
    {
        public static TimeSpan Profile => TimeSpan.FromHours(1);

        public static TimeSpan Roles => TimeSpan.FromHours(4);

        public static TimeSpan Permissions => TimeSpan.FromHours(4);
    }
}
