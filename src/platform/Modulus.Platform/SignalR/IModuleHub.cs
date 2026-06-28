namespace Modulus.SignalR.Abstractions;

using Microsoft.AspNetCore.Routing;

public interface IModuleHub
{
    void MapHub(IEndpointRouteBuilder app);
}