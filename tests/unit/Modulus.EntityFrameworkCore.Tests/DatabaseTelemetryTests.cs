namespace Modulus.EntityFrameworkCore.Tests;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Data.Abstractions;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using Modulus.Observability;
using FluentAssertions;
using Xunit;

/// <summary>
/// The database telemetry of a module context: every EF Core command is timed per operation and provider, failures are
/// counted by exception type, and the outbox depth gauge reports the absolute backlog per source.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DatabaseTelemetryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _root;

    public DatabaseTelemetryTests()
    {
        _connection = new SqliteConnection($"DataSource=telemetry-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, HostTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddModuleDatabase<TelemetryCtx>(o => o.UseSqlite(_connection));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_query_is_timed_with_its_operation_and_provider()
    {
        using var listener = Listen("modulus.db.command.duration");
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TelemetryCtx>();
        await ctx.Database.EnsureCreatedAsync();

        _ = await ctx.Items.CountAsync();

        var measured = listener.Measurements.Where(m => Tag(m, "db.operation") == "SELECT").ToList();
        measured.Should().NotBeEmpty();
        measured.Should().Contain(m => Tag(m, "db.system")!.Contains("Sqlite"), "the provider is named by the context, or by the connection when EF gives no context");
    }

    [Fact]
    public async Task A_failing_command_is_counted_by_exception_type_without_its_text()
    {
        using var listener = Listen("modulus.db.command.errors");
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TelemetryCtx>();
        await ctx.Database.EnsureCreatedAsync();

        var act = () => ctx.Database.ExecuteSqlRawAsync("SELEC no_such_table_for_telemetry");
        await act.Should().ThrowAsync<Exception>();

        var failures = listener.Measurements.Where(m => Tag(m, "db.operation") == "SELEC").ToList();
        failures.Should().NotBeEmpty();
        Tag(failures[0], "error.type").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_outbox_depth_gauge_reports_the_latest_absolute_value_per_source()
    {
        const string source = "telemetry-test-source";
        using var listener = Listen("modulus.outbox.depth");

        ModulusMeters.ReportOutboxDepth(source, 7);
        ModulusMeters.ReportOutboxDepth(source, 3);
        listener.Observe();

        var latest = listener.Measurements.Where(m => Tag(m, "source") == source).ToList();
        latest.Should().ContainSingle();
        latest[0].Value.Should().Be(3, "the gauge reports the current backlog, not a sum of reports");
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static string? Tag(Measure m, string key) => m.Tags.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <summary>Records every measurement of one instrument (by name) until disposed.</summary>
    private static Recorder Listen(string instrumentName)
    {
        var recorder = new Recorder(instrumentName);
        recorder.Listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        recorder.Listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => recorder.Add(instrument.Name, value, tags));
        recorder.Listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => recorder.Add(instrument.Name, value, tags));
        recorder.Listener.Start();
        return recorder;
    }

    private sealed record Measure(double Value, IReadOnlyDictionary<string, object?> Tags);

    private sealed class Recorder : IDisposable
    {
        private readonly string _instrumentName;
        private readonly ConcurrentBag<Measure> _measurements = [];

        public Recorder(string instrumentName)
        {
            _instrumentName = instrumentName;
            Listener = new MeterListener();
        }

        public MeterListener Listener { get; }

        public IEnumerable<Measure> Measurements => _measurements;

        public void Add(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (instrument != _instrumentName)
                return;
            var map = new Dictionary<string, object?>();
            foreach (var tag in tags)
                map[tag.Key] = tag.Value;
            _measurements.Add(new Measure(value, map));
        }

        public void Observe() => Listener.RecordObservableInstruments();

        public void Dispose() => Listener.Dispose();
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => false;
        public bool IsHost => true;
        public IDisposable Change(TenantInfo? tenant) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class TelemetryCtx(
        DbContextOptions<TelemetryCtx> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "tel_";
        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class Item
    {
        public int Id { get; set; }
    }
}
