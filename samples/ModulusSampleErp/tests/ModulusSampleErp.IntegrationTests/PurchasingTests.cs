using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ModulusSampleErp.IntegrationTests;

[Collection("API Tests")]
public class PurchasingTests
{
    private readonly ApiFixture _fixture;

    public PurchasingTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateRequisition_WithValidData_ReturnsCreated()
    {
        // Arrange
        var buyerId = "10000000-0000-0000-0000-000000000004";
        _fixture.SetUser(buyerId);

        var createRequest = new
        {
            requisitionNumber = $"REQ-{Guid.NewGuid():N}",
            orgUnitId = "00000000-0000-0000-0000-000000000111"
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/purchase-requisitions", createRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadAsAsync<dynamic>();
        Assert.NotNull(result?.id);
    }

    [Fact]
    public async Task ApproveSelfRequisition_ViolatesSoD_ReturnsBadRequest()
    {
        // Arrange
        var buyerId = "10000000-0000-0000-0000-000000000004";
        _fixture.SetUser(buyerId);

        // Create a requisition
        var createRequest = new
        {
            requisitionNumber = $"REQ-{Guid.NewGuid():N}",
            orgUnitId = "00000000-0000-0000-0000-000000000111"
        };

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/purchase-requisitions", createRequest);
        var createResult = await createResponse.Content.ReadAsAsync<dynamic>();
        var requisitionId = createResult!.id;

        // Submit the requisition
        await _fixture.Client.PostAsync($"/api/purchase-requisitions/{requisitionId}/submit", null);

        // Act - Try to approve as same user (SoD violation)
        var approveRequest = new { approverId = buyerId };
        var response = await _fixture.Client.PostAsJsonAsync(
            $"/api/purchase-requisitions/{requisitionId}/approve",
            approveRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsAsync<dynamic>();
        Assert.Contains("Segregation of Duties", error?.error?.ToString() ?? "");
    }

    [Fact]
    public async Task ApproveRequisition_ByDifferentUser_Succeeds()
    {
        // Arrange
        var buyerId = "10000000-0000-0000-0000-000000000004";
        var purchasingManagerId = "10000000-0000-0000-0000-000000000005";

        _fixture.SetUser(buyerId);

        // Create a requisition
        var createRequest = new
        {
            requisitionNumber = $"REQ-{Guid.NewGuid():N}",
            orgUnitId = "00000000-0000-0000-0000-000000000111"
        };

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/purchase-requisitions", createRequest);
        var createResult = await createResponse.Content.ReadAsAsync<dynamic>();
        var requisitionId = createResult!.id;

        // Submit
        await _fixture.Client.PostAsync($"/api/purchase-requisitions/{requisitionId}/submit", null);

        // Act - Approve as different user
        var approveRequest = new { approverId = purchasingManagerId };
        var response = await _fixture.Client.PostAsJsonAsync(
            $"/api/purchase-requisitions/{requisitionId}/approve",
            approveRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
