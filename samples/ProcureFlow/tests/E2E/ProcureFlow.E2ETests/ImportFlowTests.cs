using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ProcureFlow.E2ETests;

[Collection("API Tests")]
public class ImportFlowTests
{
    private const string TenantId = "10000000-0000-0000-0000-000000000001";
    private const string UserId = "10000000-0000-0000-0000-000000000004";

    private readonly ApiFixture _fixture;

    public ImportFlowTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateImportFile_ThenAdvanceToInstrumented()
    {
        _fixture.SetUser(UserId, tenantId: TenantId, permissions: "data:scope:bypass");

        var createRequest = new
        {
            companyId = "20000000-0000-0000-0000-000000000001",
            fiscalYear = 2026,
            incoterm = "CIF",
            currency = "USD",
            portOfLoading = "Shanghai",
            portOfDischarge = "Chittagong",
            estimatedGoodsValue = 100_000m
        };

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/import-files", createRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string? fileIdString = ExtractId(createResponse, "id");
        Assert.False(string.IsNullOrEmpty(fileIdString), "Create response did not contain an id");
        Guid fileId = Guid.Parse(fileIdString);

        var poId = Guid.NewGuid();
        var linkResponse = await _fixture.Client.PostAsJsonAsync(
            $"/api/v1/import-files/{fileId}/link-po", new { poId });
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);

        var piId = Guid.NewGuid();
        var acceptPiResponse = await _fixture.Client.PostAsJsonAsync(
            $"/api/v1/import-files/{fileId}/accept-pi", new { piId });
        Assert.Equal(HttpStatusCode.OK, acceptPiResponse.StatusCode);

        var instrumentResponse = await _fixture.Client.PostAsJsonAsync(
            $"/api/v1/import-files/{fileId}/instrument", new { lcId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, instrumentResponse.StatusCode);

        string body = await instrumentResponse.Content.ReadAsStringAsync();
        Assert.Contains("FinanceInstrumented", body);
    }

    [Fact]
    public async Task InstrumentWithoutLcOrTt_ReturnsUnprocessable()
    {
        _fixture.SetUser(UserId, tenantId: TenantId, permissions: "data:scope:bypass");

        var createRequest = new
        {
            companyId = "20000000-0000-0000-0000-000000000001",
            fiscalYear = 2026,
            incoterm = "CIF",
            currency = "USD",
            portOfLoading = "Shanghai",
            portOfDischarge = "Chittagong",
            estimatedGoodsValue = 100_000m
        };

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/v1/import-files", createRequest);
        string? createId = ExtractId(createResponse, "id");
        Assert.False(string.IsNullOrEmpty(createId), "Create response did not contain an id");
        Guid fileId = Guid.Parse(createId);

        await _fixture.Client.PostAsJsonAsync($"/api/v1/import-files/{fileId}/link-po", new { poId = Guid.NewGuid() });
        await _fixture.Client.PostAsJsonAsync($"/api/v1/import-files/{fileId}/accept-pi", new { piId = Guid.NewGuid() });

        var instrumentResponse = await _fixture.Client.PostAsJsonAsync(
            $"/api/v1/import-files/{fileId}/instrument", new { lcId = (Guid?)null, ttId = (Guid?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, instrumentResponse.StatusCode);
    }

    private static string? ExtractId(HttpResponseMessage response, string property)
    {
        using JsonDocument document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        return document.RootElement.TryGetProperty(property, out JsonElement value)
            ? value.GetString()
            : null;
    }
}