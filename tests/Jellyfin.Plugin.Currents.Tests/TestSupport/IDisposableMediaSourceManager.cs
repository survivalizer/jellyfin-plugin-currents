using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

/// <summary>Lets <see cref="InterfaceFake"/> stand in for Jellyfin's MediaSourceManager, which is IDisposable.</summary>
public interface IDisposableMediaSourceManager : IMediaSourceManager, IDisposable
{
}
