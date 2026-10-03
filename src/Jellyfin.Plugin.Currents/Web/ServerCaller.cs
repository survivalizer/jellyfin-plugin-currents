using Jellyfin.Plugin.Currents.Common;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>Decides whether a request comes from the Jellyfin server itself (ffmpeg, ffprobe, Jellyfin's own HttpClient).</summary>
public static class ServerCaller
{
    // A proxy in front of Jellyfin connects from loopback; ffmpeg and Jellyfin never send these. X-Original-For is where ASP.NET moves X-Forwarded-For.
    private static readonly string[] ForwardedHeaders = ["X-Forwarded-For", "X-Original-For", "Forwarded", "X-Real-IP"];

    public static bool IsServer(HttpContext context, LocalCallerPolicy policy) =>
        !Array.Exists(ForwardedHeaders, context.Request.Headers.ContainsKey) && policy.IsLocal(context.Connection.RemoteIpAddress);
}
