namespace Modulus.Administration;

using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Turns a domain error from an administration write into the status the route answers with, the same one the host's exception
/// handler gives, so the routes answer alike in any host.
/// </summary>
internal static class AdministrationResults
{
    public static async Task<IResult> AnswerAsync(Func<Task> action)
    {
        try
        {
            await action();
            return Results.NoContent();
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["value"] = ex.Errors.ToArray() });
        }
        catch (NotFoundException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ConflictException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
