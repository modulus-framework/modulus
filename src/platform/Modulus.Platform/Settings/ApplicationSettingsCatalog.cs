namespace Modulus.Settings;

/// <summary>
/// The application identity, branding, and regional settings the framework defines. Values are stored through the setting system: a company
/// may override the company-facing ones, and the installation-wide value is the default. Register with
/// <c>AddModulusApplicationSettings()</c>.
/// </summary>
public sealed class ApplicationSettingsCatalog : ISettingDefinitionContributor
{
    private const string General = Groups.General;
    private const string Branding = Groups.Branding;
    private const string Localization = Groups.Localization;
    private const string Communication = Groups.Communication;

    /// <summary>Group names the settings screen files these under.</summary>
    public static class Groups
    {
        public const string General = "General";
        public const string Branding = "Branding and Appearance";
        public const string Localization = "Localization and Regional Settings";
        public const string Communication = "Email and Communication";
    }

    /// <summary>Stable setting names. Changing one is a breaking change for stored values.</summary>
    public static class Names
    {
        public const string AppName = "Application.Name";
        public const string AppShortName = "Application.ShortName";
        public const string AppSubtitle = "Application.Subtitle";
        public const string AppDescription = "Application.Description";
        public const string SupportEmail = "Application.SupportEmail";
        public const string SupportUrl = "Application.SupportUrl";
        public const string PublicWebsiteUrl = "Application.PublicWebsiteUrl";
        public const string TermsOfServiceUrl = "Application.TermsOfServiceUrl";
        public const string PrivacyPolicyUrl = "Application.PrivacyPolicyUrl";
        public const string LegalNoticeUrl = "Application.LegalNoticeUrl";
        public const string MaintenanceMessage = "Application.MaintenanceMessage";
        public const string PrimaryColor = "Branding.PrimaryColor";
        public const string SecondaryColor = "Branding.SecondaryColor";
        public const string AccentColor = "Branding.AccentColor";
        public const string ThemeMode = "Branding.ThemeMode";
        public const string BorderRadius = "Branding.BorderRadius";
        public const string DefaultLanguage = "Localization.DefaultLanguage";
        public const string DefaultTimeZone = "Localization.DefaultTimeZone";
        public const string SenderName = "Email.SenderName";
        public const string SenderAddress = "Email.SenderAddress";
        public const string ReplyToAddress = "Email.ReplyToAddress";
    }

    /// <summary>The setting that holds the stored image key for a branding slot. Set through the branding endpoints, never by hand.</summary>
    public static string AssetSettingName(Modulus.Branding.BrandingSlot slot) => $"Branding.Asset.{slot}";

    /// <summary>Theme choices a user or company may pick.</summary>
    public static readonly string[] ThemeModes = ["light", "dark", "system"];

    /// <summary>Corner-radius presets. Arbitrary CSS is never stored.</summary>
    public static readonly string[] BorderRadii = ["none", "small", "medium", "large"];

    public void Contribute(ISettingDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        // Company-facing values may be overridden by a company; the installation-wide value is the default.
        var company = SettingScopes.Global | SettingScopes.Tenant;

        registry.Add(Define(Names.AppName, "Application name", General, company, description: "Shown in the page title, the navigation bar, sign-in pages and emails."));
        registry.Add(Define(Names.AppShortName, "Short name", General, company, description: "Used where the full name does not fit, such as a collapsed sidebar."));
        registry.Add(Define(Names.AppSubtitle, "Subtitle", General, company));
        registry.Add(Define(Names.AppDescription, "Description", General, company));
        registry.Add(Define(Names.SupportEmail, "Support email", General, company, SettingValueType.Email, "Where users are told to write for help."));
        registry.Add(Define(Names.SupportUrl, "Support URL", General, company, SettingValueType.Url));
        registry.Add(Define(Names.PublicWebsiteUrl, "Public website URL", General, company, SettingValueType.Url));
        registry.Add(Define(Names.TermsOfServiceUrl, "Terms of service URL", General, company, SettingValueType.Url));
        registry.Add(Define(Names.PrivacyPolicyUrl, "Privacy policy URL", General, company, SettingValueType.Url));
        registry.Add(Define(Names.LegalNoticeUrl, "Legal notice URL", General, company, SettingValueType.Url));
        registry.Add(Define(Names.MaintenanceMessage, "Maintenance message", General, SettingScopes.Global, description: "Shown while the application is in maintenance."));

        registry.Add(Define(Names.PrimaryColor, "Primary colour", Branding, company, SettingValueType.Color));
        registry.Add(Define(Names.SecondaryColor, "Secondary colour", Branding, company, SettingValueType.Color));
        registry.Add(Define(Names.AccentColor, "Accent colour", Branding, company, SettingValueType.Color));
        registry.Add(Define(Names.ThemeMode, "Theme", Branding, SettingScopes.Global | SettingScopes.User, allowedValues: ThemeModes, defaultValue: "system"));
        registry.Add(Define(Names.BorderRadius, "Corner radius", Branding, company, allowedValues: BorderRadii, defaultValue: "medium"));

        registry.Add(Define(Names.DefaultLanguage, "Default language", Localization, SettingScopes.Global, description: "A culture name such as en or fr-FR.", defaultValue: "en"));
        registry.Add(Define(Names.DefaultTimeZone, "Default time zone", Localization, company, description: "An IANA or Windows time-zone identifier.", defaultValue: "UTC"));

        foreach (var slot in Enum.GetValues<Modulus.Branding.BrandingSlot>())
            registry.Add(Define(AssetSettingName(slot), $"{slot} image", Branding, company, description: "The stored image key. Managed by the branding endpoints; the key is not a path the application reads anywhere else."));

        registry.Add(Define(Names.SenderName, "Sender name", Communication, SettingScopes.Global));
        registry.Add(Define(Names.SenderAddress, "Sender address", Communication, SettingScopes.Global, SettingValueType.Email));
        registry.Add(Define(Names.ReplyToAddress, "Reply-to address", Communication, SettingScopes.Global, SettingValueType.Email));
    }

    private static SettingDefinition Define(
        string name,
        string displayName,
        string group,
        SettingScopes scopes,
        SettingValueType valueType = SettingValueType.String,
        string? description = null,
        string? defaultValue = null,
        string[]? allowedValues = null)
        => new(name, defaultValue, displayName, description)
        {
            ValueType = valueType,
            AllowedScopes = scopes,
            Group = group,
            AllowedValues = allowedValues ?? [],
        };
}
