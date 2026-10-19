using FluentAssertions;
using Modulus.Authorization.Dimensions;
using Modulus.Core.Abstractions.Dimensions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A record may name only a dimension code registered for its company; the refusal names the field.</summary>
[Trait("Category", "Unit")]
public sealed class DimensionRecordValidationTests
{
    private sealed class MemoryRegistry : IDimensionValueRegistry
    {
        private readonly Dictionary<(string Dimension, string Code), Guid> _values = [];

        public Guid Add(string dimension, string code)
        {
            var id = Guid.NewGuid();
            _values[(dimension, code)] = id;
            return id;
        }

        public Task<DimensionValue> RegisterAsync(string dimension, string code, string label, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Guid?> ResolveIdAsync(string dimension, string code, CancellationToken ct = default)
            => Task.FromResult<Guid?>(_values.TryGetValue((dimension, code), out var id) ? id : null);

        public Task<IReadOnlyList<DimensionValue>> ListAsync(string dimension, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DimensionValue>>(
                _values.Where(v => v.Key.Dimension == dimension)
                    .Select(v => new DimensionValue(v.Value, dimension, v.Key.Code, v.Key.Code)).ToList());

        public Task<DimensionValue> SetParentAsync(string dimension, string code, string? parentDimension, string? parentCode, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_registered_code_resolves_to_the_id_the_record_carries()
    {
        var registry = new MemoryRegistry();
        var id = registry.Add("warehouse", "WH-01");

        var resolved = await registry.RequireRegisteredAsync("warehouse", "WarehouseCode", "WH-01");

        resolved.Should().Be(id);
    }

    [Fact]
    public async Task The_code_is_matched_in_canonical_form()
    {
        var registry = new MemoryRegistry();
        var id = registry.Add("warehouse", "WH-01");

        var resolved = await registry.RequireRegisteredAsync("warehouse", "WarehouseCode", "  wh-01 ");

        resolved.Should().Be(id);
    }

    [Fact]
    public async Task An_unregistered_code_refuses_the_write_naming_the_field()
    {
        var registry = new MemoryRegistry();
        registry.Add("warehouse", "WH-01");

        var act = () => registry.RequireRegisteredAsync("warehouse", "WarehouseCode", "WH-99");

        var error = await act.Should().ThrowAsync<ValidationException>();
        error.Which.FieldErrors.Should().ContainKey("WarehouseCode");
        error.Which.FieldErrors["WarehouseCode"].Should().ContainSingle().Which.Should().Contain("WH-99");
    }

    [Fact]
    public async Task A_code_registered_in_another_dimension_is_not_accepted()
    {
        var registry = new MemoryRegistry();
        registry.Add("region", "EU");

        var act = () => registry.RequireRegisteredAsync("warehouse", "RegionCode", "EU");

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class Shipment
    {
        [Dimension("warehouse")]
        public Guid? WarehouseId { get; set; }

        [Dimension("region")]
        public Guid RegionId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task A_record_whose_dimension_ids_are_registered_passes()
    {
        var registry = new MemoryRegistry();
        var region = registry.Add("region", "NORTH");
        var warehouse = registry.Add("warehouse", "WH-01");

        var act = () => registry.EnsureRecordDimensionsAsync(new Shipment { WarehouseId = warehouse, RegionId = region });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task An_unset_optional_dimension_is_not_checked()
    {
        var registry = new MemoryRegistry();
        var region = registry.Add("region", "NORTH");

        var act = () => registry.EnsureRecordDimensionsAsync(new Shipment { WarehouseId = null, RegionId = region });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_dimension_id_not_registered_for_the_company_refuses_the_record_naming_the_property()
    {
        var registry = new MemoryRegistry();
        var region = registry.Add("region", "NORTH");

        var act = () => registry.EnsureRecordDimensionsAsync(new Shipment { WarehouseId = Guid.NewGuid(), RegionId = region });

        var error = await act.Should().ThrowAsync<ValidationException>();
        error.Which.FieldErrors.Should().ContainKey("WarehouseId");
    }

    [Fact]
    public async Task An_id_registered_in_another_dimension_is_refused()
    {
        var registry = new MemoryRegistry();
        var region = registry.Add("region", "NORTH");

        var act = () => registry.EnsureRecordDimensionsAsync(new Shipment { WarehouseId = region, RegionId = region });

        var error = await act.Should().ThrowAsync<ValidationException>();
        error.Which.FieldErrors.Should().ContainKey("WarehouseId");
    }

    [Fact]
    public async Task A_dimension_attribute_on_a_non_guid_property_is_a_programming_error()
    {
        var registry = new MemoryRegistry();

        var act = () => registry.EnsureRecordDimensionsAsync(new BadRecord());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class BadRecord
    {
        [Dimension("warehouse")]
        public string Code { get; set; } = "WH-01";
    }

    [Fact]
    public async Task A_malformed_code_is_refused_as_a_validation_error_on_its_field()
    {
        var registry = new MemoryRegistry();

        var act = () => registry.RequireRegisteredAsync("warehouse", "WarehouseCode", new string('x', 65));

        var error = await act.Should().ThrowAsync<ValidationException>();
        error.Which.FieldErrors.Should().ContainKey("WarehouseCode");
    }
}
