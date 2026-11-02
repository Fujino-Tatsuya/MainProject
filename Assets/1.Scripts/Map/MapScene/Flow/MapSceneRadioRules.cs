using System;
using System.Collections.Generic;

/// <summary>
/// [서버] 맵씬 일반 무전 재생 기록(순수 — EditMode 테스트 <c>MapSceneRadioRulesTests</c>). 같은 id 는 판마다 한 번(명세 §8.1 — 영역 재진입·중복 사건 무시).
/// 새 판 = 새 디렉터(씬 로드)라 새 기록이다.
/// </summary>
public sealed class MapSceneRadioLog
{
    private readonly HashSet<string> _played = new HashSet<string>(StringComparer.Ordinal);

    public bool HasPlayed(string id) => !string.IsNullOrEmpty(id) && _played.Contains(id);

    /// <summary>처음이면 기록하고 true. 빈 id·이미 튼 id 는 false.</summary>
    public bool TryPlay(string id) => !string.IsNullOrEmpty(id) && _played.Add(id);
}

/// <summary>[모든 피어] 일반 무전 표시 판정(순수).</summary>
public static class MapSceneRadioRules
{
    /// <summary>
    /// 지금 무전 줄을 보여 주나. 줄은 <paramref name="lineStart"/>(ServerTime)부터 <paramref name="lineSeconds"/> 동안.
    /// 소개 연출(2·4구역)이 돌고 있으면 숨기고(연출 대사와 겹치지 않게), 줄이 시작된 <b>뒤에</b> 소개가 시작됐으면 그 줄은 버린다
    /// (명세 §8.1 — 강제 소개가 시작되면 기존 일반 무전을 정리한다. 연출이 끝나도 다시 나오지 않는다).
    /// <paramref name="introStarts"/> = 시작한 적 있는 소개의 시작 ServerTime 들(아직이면 넣지 않는다).
    /// </summary>
    public static bool ShouldShow(double lineStart, float lineSeconds, double now, bool introRunning, IReadOnlyList<double> introStarts)
    {
        if (introRunning || now < lineStart || now >= lineStart + lineSeconds) return false;
        if (introStarts != null)
            for (int i = 0; i < introStarts.Count; i++)
                if (introStarts[i] >= lineStart && introStarts[i] <= now) return false;
        return true;
    }
}

/// <summary>[서버] 3구역 상대 첫 인식 거리 판정(순수). 수평 거리 + 층 차이 — 다른 층(위·아래)의 몬스터는 화면에 안 보인다.</summary>
public static class MapSceneZone3SightRules
{
    /// <summary>층이 다르다고 보는 높이 차(m).</summary>
    public const float MaxHeightDifference = 4f;

    public static bool InSight(UnityEngine.Vector3 player, UnityEngine.Vector3 monster, float range)
    {
        float dx = monster.x - player.x, dz = monster.z - player.z;
        return dx * dx + dz * dz <= range * range && System.Math.Abs(monster.y - player.y) <= MaxHeightDifference;
    }
}
