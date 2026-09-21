using Meetup.Modules.Administration.Infrastructure;
using Meetup.Modules.Administration.Presentation;
using Meetup.Modules.Administration.Web;
using Meetup.Modules.Meetings.Infrastructure;
using Meetup.Modules.Meetings.Presentation;
using Meetup.Modules.Meetings.Web;
using Meetup.Modules.Payments.Infrastructure;
using Meetup.Modules.Payments.Presentation;
using Meetup.Modules.Payments.Web;
using Meetup.Modules.Registrations.Infrastructure;
using Meetup.Modules.Registrations.Presentation;
using Meetup.Modules.Registrations.Web;
using Meetup.Modules.UserAccess.Infrastructure;
using Meetup.Modules.UserAccess.Presentation;
using Meetup.Modules.UserAccess.Web;
using Meetup.Shared.Presentation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Modulus.AspNetCore.Correlation;
using Modulus.AspNetCore.Endpoints;
using Modulus.AspNetCore.Extensions;
using Modulus.AspNetCore.HealthChecks;
using Modulus.AspNetCore.Idempotency;
using Modulus.AspNetCore.OpenApi;
using Modulus.AspNetCore.Security;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Identity.Authentik;
using Modulus.Mediator.Extensions;
using Modulus.UI;
using Modulus.Localization;
using Modulus.UI.Identity;
using Modulus.UI.Permissions;
using Modulus.UI.Tenancy;
using Modulus.UI.Users;
using Scalar.AspNetCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ── Modulus module system ──────────────────────────────────────
builder.Services.AddModulus(builder.Configuration, modules => modules
    .AddModule<RegistrationsModule>()
    .AddModule<UserAccessModule>()
    .AddModule<AdministrationModule>()
    .AddModule<PaymentsModule>()
    .AddModule<MeetingsModule>());
builder.Services.AddModulusExceptionHandling();

// ── Cross-cutting concerns ─────────────────────────────────────
builder.Services.AddModulusCorrelation(builder.Configuration);
builder.Services.AddModulusIdempotency(builder.Configuration);
builder.Services.AddModulusSecurityHeaders(builder.Configuration);

// ── UI modules ───────────────────────────────────────────────────
builder.Services.AddModulusLocalization();
builder.Services.AddModulusUi();
builder.Services.AddModulusIdentityUi(builder.Configuration);
builder.Services.AddModulusPermissionsUi(builder.Configuration);
builder.Services.AddModulusUsersUi(builder.Configuration);
builder.Services.AddModulusTenancyUi(builder.Configuration);
builder.Services.AddUiModule<MeetingsWebModule>();
builder.Services.AddUiModule<RegistrationsWebModule>();
builder.Services.AddUiModule<PaymentsWebModule>();
builder.Services.AddUiModule<UserAccessWebModule>();
builder.Services.AddUiModule<AdministrationWebModule>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddModulusEvents(typeof(Program).Assembly);
builder.Services.AddMediator();
builder.Services.AddModulusOpenApi(builder.Configuration);

// ── Authentik (sole identity provider, PKCE-only public client) ──
// Every endpoint requires a JWT minted by the Authentik `meetup`
// application (per-app issuer .../application/o/meetup/) via the
// authorization-code + PKCE flow — there is no client secret anywhere:
// no password grant, no confidential client. The OIDC adapter registers
// the framework's discovery-based token validator
// (IExternalIdentityProvider); JwtBearer enforces it on each request.
var authentikSection = builder.Configuration.GetSection("Identity:ExternalProviders:Authentik");
var authentikAuthority = authentikSection["Authority"];
var authentikClientId = authentikSection["ClientId"];
var authAudience = authentikSection["Audience"];
if (string.IsNullOrWhiteSpace(authentikAuthority)
    || string.IsNullOrWhiteSpace(authentikClientId)
    || string.IsNullOrWhiteSpace(authAudience))
    throw new InvalidOperationException(
        "Authentik is not configured. Set Identity:ExternalProviders:Authentik " +
        "(Authority, ClientId, Audience) — see README § Authentication.");
if (!string.IsNullOrWhiteSpace(authentikSection["ClientSecret"]))
    throw new InvalidOperationException(
        "Authentik must be PKCE-only: remove Identity:ExternalProviders:Authentik:ClientSecret. " +
        "The `meetup` provider in Authentik is a public client (no secret).");
// Per-application issuer, e.g. http://localhost:9000/application/o/meetup/.
// NOTE: the Authentik application slug must equal the OAuth client id.
var authAuthority = $"{authentikAuthority.TrimEnd('/')}/application/o/{authentikClientId}/";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authAuthority;
        options.Audience = authAudience;
        // Authentik serves plain HTTP locally. HTTPS metadata stays enforced
        // for non-loopback authorities in Production; loopback is always
        // allowed so the sample boots against local Authentik over HTTP
        // regardless of ASPNETCORE_ENVIRONMENT.
        options.RequireHttpsMetadata = builder.Environment.IsProduction()
            && !(Uri.TryCreate(authAuthority, UriKind.Absolute, out var authUri)
                && authUri.IsLoopback);
        // Prevent legacy claim-rewriting; Authentik carries groups natively.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "groups";
        options.Events = new JwtBearerEvents
        {
            // The framework's permission resolver reads ClaimTypes.Role/"role"
            // while Authentik emits "groups": mirror each group onto a role
            // claim so Roles(...) AND Permissions(...) both see Authentik
            // groups. IsInRole keeps using "groups" via RoleClaimType above.
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    // Materialize before mutating: the query lazily walks the
                    // identity's claim list, which the loop below appends to.
                    var groups = identity.FindAll("groups").Select(c => c.Value).Distinct().ToList();
                    foreach (var group in groups)
                    {
                        if (!identity.HasClaim(ClaimTypes.Role, group))
                            identity.AddClaim(new Claim(ClaimTypes.Role, group));
                    }
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddAuthentik(builder.Configuration);
// Configure (runs BEFORE the OIDC post-configure validation): the OIDC
// handler is interactive-only in this API (tokens are validated by
// JwtBearer), but AuthenticationMiddleware eagerly initializes every
// request-handler scheme on each request — with an http Authority and the
// default RequireHttpsMetadata=true that alone 500s the whole API. Same
// loopback rule as JwtBearer above. NOTE: PostConfigure would be too late
// here — the built-in OpenIdConnectPostConfigureOptions throws during its
// own run, before any later post-configure could disable the check.
builder.Services.Configure<OpenIdConnectOptions>("Authentik", options =>
{
    options.RequireHttpsMetadata = builder.Environment.IsProduction()
        && !(Uri.TryCreate(options.Authority, UriKind.Absolute, out var oidcUri)
            && oidcUri.IsLoopback);
});

// ── Roles + permissions ────────────────────────────────────────
// Authentik groups (MeetupRoles) are the roles; the grant store maps them
// to the modules' permission catalogs. Endpoints demand BOTH the role and
// the permission (AND semantics), so a stray grant alone never opens an
// endpoint and a bare role membership is never enough.
builder.Services.AddModulusAuthorization();
builder.Services.AddPermissions("registrations", registry =>
{
    registry.Add(RegistrationsPermissions.RegisterUser, "Registers a new user");
    registry.Add(RegistrationsPermissions.ConfirmRegistration, "Confirms a pending registration");
    registry.Add(RegistrationsPermissions.ViewRegistrations, "Lists user registrations");
});
builder.Services.AddPermissions("useraccess", registry =>
{
    registry.Add(UserAccessPermissions.ViewUsers, "Lists system users");
    registry.Add(UserAccessPermissions.DeactivateUser, "Deactivates a user");
});
builder.Services.AddPermissions("administration", registry =>
{
    registry.Add(AdministrationPermissions.ProposeMeetingGroup, "Proposes a new meeting group");
    registry.Add(AdministrationPermissions.DecideProposal, "Accepts or rejects a proposal");
    registry.Add(AdministrationPermissions.ViewProposals, "Lists meeting group proposals");
});
builder.Services.AddPermissions("meetings", registry =>
{
    registry.Add(MeetingsPermissions.CreateMeeting, "Creates a meeting");
    registry.Add(MeetingsPermissions.JoinMeeting, "Joins a meeting");
    registry.Add(MeetingsPermissions.Comment, "Comments on a meeting");
    registry.Add(MeetingsPermissions.ViewMeetings, "Lists groups, meetings and attendees");
});
builder.Services.AddPermissions("payments", registry =>
{
    registry.Add(PaymentsPermissions.BuySubscription, "Buys a subscription");
    registry.Add(PaymentsPermissions.PayMeetingFee, "Pays a meeting's event fee");
    registry.Add(PaymentsPermissions.ViewPayments, "Lists subscriptions");
});
builder.Services.AddPermissionGrants(store =>
{
    // Every app user is in meetup-members (assign the group in Authentik).
    store.GrantToRole(MeetupRoles.Members,
        RegistrationsPermissions.RegisterUser,
        MeetingsPermissions.ViewMeetings,
        MeetingsPermissions.JoinMeeting,
        MeetingsPermissions.Comment,
        PaymentsPermissions.BuySubscription,
        PaymentsPermissions.PayMeetingFee);
    // Organizers additionally propose groups and create meetings.
    store.GrantToRole(MeetupRoles.Organizers,
        RegistrationsPermissions.RegisterUser,
        AdministrationPermissions.ProposeMeetingGroup,
        AdministrationPermissions.ViewProposals,
        MeetingsPermissions.ViewMeetings,
        MeetingsPermissions.JoinMeeting,
        MeetingsPermissions.Comment,
        MeetingsPermissions.CreateMeeting,
        PaymentsPermissions.BuySubscription,
        PaymentsPermissions.PayMeetingFee);
    // Administrators hold every permission in the catalog.
    store.GrantToRole(MeetupRoles.Admins,
        RegistrationsPermissions.RegisterUser,
        RegistrationsPermissions.ConfirmRegistration,
        RegistrationsPermissions.ViewRegistrations,
        UserAccessPermissions.ViewUsers,
        UserAccessPermissions.DeactivateUser,
        AdministrationPermissions.ProposeMeetingGroup,
        AdministrationPermissions.DecideProposal,
        AdministrationPermissions.ViewProposals,
        MeetingsPermissions.CreateMeeting,
        MeetingsPermissions.JoinMeeting,
        MeetingsPermissions.Comment,
        MeetingsPermissions.ViewMeetings,
        PaymentsPermissions.BuySubscription,
        PaymentsPermissions.PayMeetingFee,
        PaymentsPermissions.ViewPayments);
});
builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.MigrateModulusDatabasesAsync(
    app.Environment.IsProduction()
        ? DatabaseInitializationMode.Migrate
        : DatabaseInitializationMode.MigrateOrCreate);

app.UseForwardedHeaders();
app.UseModulusCorrelation();
app.UseModulusSecurityHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseModulus();
app.UseModulusIdempotency();

// ── UI middleware ───────────────────────────────────────────────
app.UseStaticFiles();
app.MapRazorPages();
app.MapModulusUiMenu();

// No host home page — land on the first module screen.
app.MapGet("/", () => Results.Redirect("/Meetings"));

app.MapModulusEndpoints(
    typeof(Meetup.Modules.Registrations.Presentation.RegistrationsPermissions).Assembly,
    typeof(Meetup.Modules.UserAccess.Presentation.UserAccessPermissions).Assembly,
    typeof(Meetup.Modules.Administration.Presentation.AdministrationPermissions).Assembly,
    typeof(Meetup.Modules.Meetings.Presentation.MeetingsPermissions).Assembly,
    typeof(Meetup.Modules.Payments.Presentation.PaymentsPermissions).Assembly);

// ── UI module endpoints ─────────────────────────────────────────
app.MapModulusIdentityUi();
app.MapModulusPermissionsUi();
app.MapModulusUsersUi();
app.MapModulusTenancyUi();

app.MapModulusHealthChecks();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Run();
