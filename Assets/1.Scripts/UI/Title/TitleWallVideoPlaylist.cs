using System.Collections.Generic;

/// <summary>
/// 타이틀 벽 모니터 존 투어 영상 배정(할 일 254) — <b>같은 순간 모니터끼리 같은 영상을 틀지 않는다</b>.
/// 순수 함수라 EditMode 테스트로 확인한다(<c>TitleWallVideoPlaylistTests</c>).
/// </summary>
/// <remarks>
/// 규칙: 시작은 <c>start</c> 부터 연속 번호(서로 다름). 한 모니터 영상이 끝나면 그 영상 다음 번호부터 순환하며
/// <b>다른 모니터가 지금 틀고 있지 않은</b> 첫 영상. 영상 수가 모니터 수 이하라 고를 게 없으면 같은 영상을 다시 튼다.
/// </remarks>
public static class TitleWallVideoPlaylist
{
    /// <summary>모니터 <paramref name="screenCount"/> 대의 시작 영상 번호. 영상이 모니터보다 적으면 그만큼은 겹친다.</summary>
    public static int[] Initial(int screenCount, int clipCount, int start)
    {
        var result = new int[System.Math.Max(0, screenCount)];
        if (clipCount <= 0)
        {
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            return result;
        }

        int s = Mod(start, clipCount);
        for (int i = 0; i < result.Length; i++) result[i] = (s + i) % clipCount;
        return result;
    }

    /// <summary>
    /// <paramref name="current"/> 가 끝난 모니터의 다음 영상. <paramref name="playingElsewhere"/> = 다른 모니터들이 지금 트는 번호(-1 은 무시).
    /// </summary>
    public static int Next(int current, IReadOnlyList<int> playingElsewhere, int clipCount)
    {
        if (clipCount <= 0) return -1;
        int from = current < 0 ? 0 : current + 1;
        for (int k = 0; k < clipCount; k++)
        {
            int candidate = Mod(from + k, clipCount);
            if (candidate == current) continue;
            if (!Contains(playingElsewhere, candidate)) return candidate;
        }
        return current >= 0 && current < clipCount ? current : 0; // 고를 게 없다 — 같은 영상 다시
    }

    private static bool Contains(IReadOnlyList<int> list, int value)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
        return false;
    }

    private static int Mod(int a, int n) => ((a % n) + n) % n;
}
