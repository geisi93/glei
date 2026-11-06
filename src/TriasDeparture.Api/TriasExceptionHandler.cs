using System.Xml;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TriasDeparture.Api;

/// <summary>
/// Maps failures of the upstream TRIAS service to 502/504 ProblemDetails
/// instead of a generic 500.
/// </summary>
internal sealed class TriasExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            // HttpClient.Timeout surfaces as TaskCanceledException (client aborts are not handled here)
            TaskCanceledException when !context.RequestAborted.IsCancellationRequested
                => (StatusCodes.Status504GatewayTimeout, "TRIAS did not respond in time."),
            HttpRequestException
                => (StatusCodes.Status502BadGateway, "TRIAS request failed."),
            XmlException
                => (StatusCodes.Status502BadGateway, "TRIAS returned an invalid response."),
            _ => (0, null)
        };

        if (title is null)
            return false;

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext    = context,
            Exception      = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
    }
}
