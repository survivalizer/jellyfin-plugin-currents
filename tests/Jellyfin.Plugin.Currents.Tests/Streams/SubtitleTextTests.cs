using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class SubtitleTextTests
{
    private const string Srt = "1\r\n00:00:01,000 --> 00:00:02,500\r\nHello\r\n\r\n7\r\n00:00:03,000 --> 00:00:04,000\r\n<i>World</i>\r\n";
    private const string Vtt = "﻿WEBVTT\n\nNOTE a comment\n\n00:01.000 --> 00:02.500 align:start\n<v Bob>Hello &amp; bye</v>\n\nintro\n01:00:03.000 --> 01:00:04.000\n<c.yellow><i>World</i></c>\n";
    private const string Ass = "[Script Info]\nScriptType: v4.00+\n\n[V4+ Styles]\nFormat: Name, Fontname\nStyle: Default,Arial\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:03.03,0:00:04.00,Default,,0,0,0,,{\\i1}World{\\i0}, again\\Nline two\nDialogue: 0,0:00:01.00,0:00:02.50,Default,,0,0,0,,Hello\n";

    [Theory]
    [InlineData(Srt, "srt")]
    [InlineData(Vtt, "vtt")]
    [InlineData(Ass, "ass")]
    [InlineData("[Script Info]\nScriptType: v4.00\n\n[V4 Styles]\n", "ssa")]
    [InlineData("<html>not found</html>", null)]
    [InlineData("", null)]
    public void Sniffs_the_format(string text, string? expected) => Assert.Equal(expected, SubtitleText.Sniff(text));

    [Fact]
    public void Srt_is_renumbered_with_unix_newlines() =>
        Assert.Equal("1\n00:00:01,000 --> 00:00:02,500\nHello\n\n2\n00:00:03,000 --> 00:00:04,000\n<i>World</i>\n\n", SubtitleText.ToSrt(Srt));

    [Fact]
    public void WebVtt_becomes_srt_without_voice_and_class_tags() =>
        Assert.Equal("1\n00:00:01,000 --> 00:00:02,500\nHello & bye\n\n2\n01:00:03,000 --> 01:00:04,000\n<i>World</i>\n\n", SubtitleText.ToSrt(Vtt));

    [Fact]
    public void Ass_dialogue_becomes_srt_in_time_order() =>
        Assert.Equal("1\n00:00:01,000 --> 00:00:02,500\nHello\n\n2\n00:00:03,030 --> 00:00:04,000\nWorld, again\nline two\n\n", SubtitleText.ToSrt(Ass));

    [Fact]
    public void Anything_else_is_null() => Assert.Null(SubtitleText.ToSrt("<html>not found</html>"));
}
