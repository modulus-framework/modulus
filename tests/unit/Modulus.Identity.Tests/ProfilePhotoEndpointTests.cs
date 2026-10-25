namespace Modulus.Identity.Tests;

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Media;
using Modulus.MultiTenancy;
using Modulus.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

/// <summary>
/// The photo endpoints (premodule 3): a person uploads, replaces, reads and removes only their own photo; the stored file lives under the
/// account's tenant folder with a random name; the previous file is removed only after the account is saved; and a photo refused by its
/// content is never stored.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProfilePhotoEndpointTests : IAsyncLifetime
{
    private static readonly Guid s_tenant = Guid.Parse("22222222-aaaa-bbbb-cccc-333333333333");

    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private IDisposable? _host;
    private readonly InMemoryFileStorage _storage = new();

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<ICurrentTenant>(new CurrentTenant());
        services.AddSingleton<IFileStorage>(_storage);
        services.AddSingleton<IProfilePhotoProcessor, ProfilePhotoProcessor>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>().AddEntityFrameworkStores<ModulusIdentityDbContext>().AddDefaultTokenProviders();
        _provider = services.BuildServiceProvider();

        using (_provider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        }

        _scope = _provider.CreateAsyncScope();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_person_uploads_a_photo_that_is_stored_in_the_tenant_folder_and_read_back_as_a_jpeg()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync("ada@example.com");

        var uploaded = await UploadAsync(controller);

        uploaded.Should().BeOfType<OkObjectResult>();
        _storage.Files.Should().ContainSingle();
        var path = _storage.Files.Keys.Single();
        path.Should().StartWith($"profile-photos/{s_tenant:N}/");
        path.Should().EndWith(".jpg");

        var served = await controller.GetProfilePhotoAsync(default);
        served.Should().BeOfType<FileStreamResult>();
        served.As<FileStreamResult>().ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task A_replacement_removes_the_previous_file_only_after_the_account_is_saved()
    {
        EnterHost();
        var (controller, users) = await SignedInAsync("ada@example.com");
        await UploadAsync(controller);
        var first = _storage.Files.Keys.Single();

        await UploadAsync(controller);

        _storage.Files.Should().HaveCount(1, "the previous photo is gone once the replacement is in place");
        _storage.Files.Keys.Single().Should().NotBe(first, "every photo gets its own random key");
        _storage.Deleted.Should().ContainSingle(first);
        (await users.FindByEmailAsync("ada@example.com"))!.PhotoAssetId.Should().NotBeNull();
    }

    [Fact]
    public async Task A_failed_removal_of_the_old_file_does_not_fail_the_replacement()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync("ada@example.com");
        await UploadAsync(controller);
        _storage.FailDeletes = true;

        var replaced = await UploadAsync(controller);

        replaced.Should().BeOfType<OkObjectResult>("the account already points at the new photo; the leftover file is an orphan to clean up");
        _storage.Files.Should().HaveCount(2, "the leftover is still there, as the orphan the log describes");
    }

    [Fact]
    public async Task A_text_file_is_refused_by_its_content_and_nothing_is_stored()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync();
        controller.HttpContext.Request.Body = new MemoryStream("<svg/>"u8.ToArray());

        var result = await controller.UploadProfilePhotoAsync(default);

        var refused = result.Should().BeOfType<ObjectResult>().Subject;
        refused.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_a_photo_clears_the_reference_and_the_file()
    {
        EnterHost();
        var (controller, users) = await SignedInAsync("ada@example.com");
        await UploadAsync(controller);

        var removed = await controller.DeleteProfilePhotoAsync(default);

        removed.Should().BeOfType<NoContentResult>();
        _storage.Files.Should().BeEmpty();
        (await users.FindByEmailAsync("ada@example.com"))!.PhotoAssetId.Should().BeNull();
        var details = (ProfileDetailsResponse)((OkObjectResult)await controller.GetProfileDetailsAsync()).Value!;
        details.HasPhoto.Should().BeFalse();
    }

    [Fact]
    public async Task A_person_cannot_read_another_persons_photo()
    {
        EnterHost();
        var (ada, _) = await SignedInAsync("ada@example.com");
        await UploadAsync(ada);
        var (grace, _) = await SignedInAsync("grace@example.com");

        var result = await grace.GetProfilePhotoAsync(default);

        result.Should().BeOfType<NotFoundResult>("the photo is served only to the account it belongs to");
    }

    [Fact]
    public async Task Without_file_storage_the_upload_is_refused_with_503()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync();
        controller.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IProfilePhotoProcessor, ProfilePhotoProcessor>()
            .AddLogging()
            .BuildServiceProvider();
        controller.HttpContext.Request.Body = new MemoryStream(EncodePng());

        var result = await controller.UploadProfilePhotoAsync(default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>Enters the host context in the test method itself: an AsyncLocal set inside an async helper does not flow back to the test.</summary>
    private void EnterHost() => _host = _provider.GetRequiredService<ICurrentTenant>().Change(null);

    /// <summary>
    /// Creates a signed-in account in the test tenant and a controller acting as it, with a request body of a valid PNG and the
    /// application's services. Each call to the upload needs a fresh body, so the helper sets one.
    /// </summary>
    private async Task<(AccountController<ModulusUser> Controller, UserManager<ModulusUser> Users)> SignedInAsync(string email = "ada@example.com")
    {
        var users = _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = email.Split('@')[0], Email = email, IsActive = true, TenantId = s_tenant };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"));
        var httpContext = new DefaultHttpContext { User = principal, RequestServices = _provider };
        httpContext.Request.Body = new MemoryStream(EncodePng());
        var controller = new AccountController<ModulusUser>(users, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        return (controller, users);
    }

    /// <summary>Uploads a fresh valid PNG: a request body is a stream, and a second upload would otherwise read an empty one.</summary>
    private static Task<IActionResult> UploadAsync(AccountController<ModulusUser> controller)
    {
        controller.HttpContext.Request.Body = new MemoryStream(EncodePng());
        return controller.UploadProfilePhotoAsync(default);
    }

    private static byte[] EncodePng()
    {
        using var image = new Image<Rgba32>(64, 64, Color.DarkSeaGreen);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>An in-memory <see cref="IFileStorage"/>: what the test needs of a store, with a switch that makes removals fail.</summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public List<string> Deleted { get; } = [];

        public bool FailDeletes { get; set; }

        public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Files[path] = buffer.ToArray();
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Files[path]));

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            if (FailDeletes)
                throw new IOException("The store refused the removal.");
            Deleted.Add(path);
            Files.Remove(path);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(Files.ContainsKey(path));

        public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
