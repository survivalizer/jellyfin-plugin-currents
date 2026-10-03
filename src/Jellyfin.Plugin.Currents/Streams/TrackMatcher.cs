using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Finds the probed track a synthetic display index stood for.</summary>
public static class TrackMatcher
{
    /// <summary>
    /// Same type, then: same language and forced flag, else same language, at the same position among those tracks (clamped);
    /// a track without a language maps by position only when display and probe list the same number of that type.
    /// </summary>
    /// <param name="display">The display streams the client chose from.</param>
    /// <param name="probed">The probed streams.</param>
    /// <param name="index">The chosen display index.</param>
    /// <param name="expected">The type the request asked for (audio for an audio index, subtitle for a subtitle index).</param>
    /// <returns>The probed index, or null when nothing fits.</returns>
    public static int? Map(IReadOnlyList<MediaStream> display, IReadOnlyList<MediaStream> probed, int index, MediaStreamType expected)
    {
        var chosen = display.FirstOrDefault(s => s.Index == index);
        if (chosen is null || chosen.Type != expected)
        {
            return null;
        }

        var shown = display.Where(s => s.Type == chosen.Type && !s.IsExternal).ToList();
        var real = probed.Where(s => s.Type == chosen.Type && !s.IsExternal).ToList();
        var language = LanguageCodes.ToIso6392(chosen.Language);
        if (language is not null)
        {
            foreach (var strict in new[] { true, false })
            {
                bool Same(MediaStream s) => LanguageCodes.ToIso6392(s.Language) == language && (!strict || s.IsForced == chosen.IsForced);
                var candidates = real.Where(Same).ToList();
                var position = shown.Where(Same).ToList().IndexOf(chosen);
                if (candidates.Count > 0 && position >= 0)
                {
                    return candidates[Math.Min(position, candidates.Count - 1)].Index;
                }
            }

            return null;
        }

        var ordinal = shown.IndexOf(chosen);
        return shown.Count == real.Count && ordinal >= 0 ? real[ordinal].Index : null;
    }
}
