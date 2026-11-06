using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 연출 동안 <b>모든 플레이어</b>의 벽 뒤 실루엣을 끄고 끝나면 되켜는 묶음(포드 출격·착륙, 할 일 230).
/// 태그마다 <see cref="PlayerSilhouetteTag.SetSuppressed"/> 를 쓴다 — 로컬 시각 값이라 복제하지 않고, 피어마다 자기 화면에서 돈다.
/// 연출 중 매 프레임 <see cref="SuppressAll"/> 을 불러도 된다 — 스폰된 태그 수(<see cref="PlayerSilhouetteTag.ActiveCount"/>)가 바뀔 때만 다시 찾아 늦게 스폰된 플레이어도 잡는다.
/// 렌더러 피처 전체를 끄는 <see cref="PlayerSilhouetteFeature.SuppressedByCinematic"/>(보스 승리 연출)와 달리 연출끼리 서로의 끔을 풀지 않는다.
/// </summary>
public sealed class PlayerSilhouetteSuppression
{
    private readonly List<PlayerSilhouetteTag> _suppressed = new List<PlayerSilhouetteTag>();


    /// <summary>스폰된 모든 플레이어 실루엣을 끈다. 이미 다 껐으면 아무것도 안 한다.</summary>
    public void SuppressAll()
    {
        if (_suppressed.Count == PlayerSilhouetteTag.ActiveCount)
            return;

        _suppressed.RemoveAll(s => s == null);
        foreach (PlayerSilhouetteTag silhouette in Object.FindObjectsByType<PlayerSilhouetteTag>(FindObjectsSortMode.None))
        {
            if (silhouette == null || !silhouette.IsSpawned || _suppressed.Contains(silhouette)) continue;
            silhouette.SetSuppressed(true);
            _suppressed.Add(silhouette);
        }
    }

    /// <summary>꺼 둔 실루엣을 전부 되켠다(색은 보는 사람 기준으로 태그가 다시 고른다). 멱등.</summary>
    public void RestoreAll()
    {
        foreach (PlayerSilhouetteTag silhouette in _suppressed)
        {
            if (silhouette != null) silhouette.SetSuppressed(false);
        }

        _suppressed.Clear();
    }
}
