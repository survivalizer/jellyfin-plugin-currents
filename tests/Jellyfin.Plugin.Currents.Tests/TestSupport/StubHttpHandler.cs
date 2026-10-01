using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<Uri> Requests { get; } = [];

    public AuthenticationHeaderValue? LastAuthorization { get; private set; }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Redirect)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        LastAuthorization = request.Headers.Authorization;
        return Task.FromResult(_respond(request));
    }
}
