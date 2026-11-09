using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 타이틀 벽 모니터 존 투어 영상 배정(할 일 254) — 같은 순간 모니터끼리 같은 영상을 틀지 않는다.
/// </summary>
public sealed class TitleWallVideoPlaylistTests
{
    [Test]
    public void Initial_ThreeOfFive_AllDifferent([Values(0, 1, 2, 3, 4, 7, -1)] int start)
    {
        int[] clips = TitleWallVideoPlaylist.Initial(3, 5, start);

        Assert.That(clips.Length, Is.EqualTo(3));
        Assert.That(new HashSet<int>(clips).Count, Is.EqualTo(3));
        foreach (int c in clips) Assert.That(c, Is.InRange(0, 4));
    }

    [Test]
    public void Initial_NoClips_AllMinusOne()
    {
        Assert.That(TitleWallVideoPlaylist.Initial(3, 0, 0), Is.EqualTo(new[] { -1, -1, -1 }));
    }

    [Test]
    public void Next_SkipsClipsPlayingElsewhere()
    {
        // 0 이 끝났다, 다른 모니터 = 1·2 → 3
        Assert.That(TitleWallVideoPlaylist.Next(0, new[] { 1, 2 }, 5), Is.EqualTo(3));
        // 4 가 끝났다, 다른 모니터 = 0·1 → 감아서 2
        Assert.That(TitleWallVideoPlaylist.Next(4, new[] { 0, 1 }, 5), Is.EqualTo(2));
    }

    [Test]
    public void Next_NoOtherChoice_ReplaysSame()
    {
        // 영상 3개·모니터 3대 — 끝난 모니터는 자기 영상을 다시 튼다
        Assert.That(TitleWallVideoPlaylist.Next(1, new[] { 0, 2 }, 3), Is.EqualTo(1));
    }

    [Test]
    public void Simulation_NeverOverlaps_AndVisitsAllClips()
    {
        const int clipCount = 5;
        int[] playing = TitleWallVideoPlaylist.Initial(3, clipCount, 2);
        var seen = new HashSet<int>(playing);
        var others = new List<int>();

        // 끝나는 순서를 섞어 가며 60번 교체
        int[] order = { 0, 2, 1, 1, 0, 2, 2, 0, 1, 0 };
        for (int step = 0; step < 60; step++)
        {
            int screen = order[step % order.Length];
            others.Clear();
            for (int i = 0; i < playing.Length; i++) if (i != screen) others.Add(playing[i]);

            playing[screen] = TitleWallVideoPlaylist.Next(playing[screen], others, clipCount);
            seen.Add(playing[screen]);

            Assert.That(new HashSet<int>(playing).Count, Is.EqualTo(3), $"step {step}: {string.Join(",", playing)}");
        }

        Assert.That(seen.Count, Is.EqualTo(clipCount));
    }
}
