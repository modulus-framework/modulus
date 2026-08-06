namespace ModulusSample.Shared.Application.Caching;

public static class CacheKeys
{
    public static class User
    {
        private const string Prefix = "user";

        public static string UserProfile(Guid userId) =>
            $"{Prefix}:profile:{userId}";

        public static string UserByEmail(string email) =>
            $"{Prefix}:email:{email.ToLowerInvariant()}";

        public static string UserRoles(Guid userId) =>
            $"{Prefix}:roles:{userId}";

        public static string UserPermissions(Guid userId) =>
            $"{Prefix}:permissions:{userId}";

        public static string MyPermissionsResponse(Guid userId) =>
            $"{Prefix}:mypermissionsresponse:{userId}";

        public static string AllRoles() =>
            $"{Prefix}:roles:all";

        public static string UserContext(Guid authentikId) =>
            $"{Prefix}:context:kc:{authentikId}";

        public static string UserDataPrefix(Guid userId) => $"{Prefix}:{userId}";
        public static string AllRolesPrefix() => $"{Prefix}:roles";
        public static string AllPermissionsPrefix() => $"{Prefix}:permissions";
    }
}
