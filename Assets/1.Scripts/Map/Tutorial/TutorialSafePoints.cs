using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 스텝(구간)별 고정 안전 지점(할 일 80 — 1안). <b>4.MapScene 상주(비네트워크)</b>, 동작은 서버만.
/// 규칙은 순수 <see cref="TutorialSafePointRules"/>. 문서: Docs/tech/tutorial-director.md §12.
///
/// 지점 = 이 컴포넌트의 <see cref="points"/>(구간 → 씬 Transform). 저작 메뉴 <c>Tools/Map/Authoring/Tutorial/8. Place Safe Points</c> 가
/// 빠진 구간만 기본 위치(들어오는 배리어 안쪽)에 만든다 — 옮긴 지점은 다시 돌려도 그대로다.
///
/// 언제 되돌리나(튜토리얼 진행 중 · 단계 Finished 전 · 보스방 이동 중 아님):
///  · 낙사 — 기존 추락 복구(<see cref="PlayerFallRecovery"/>)가 복귀 지점을 물을 때(<see cref="PlayerSafePointTracker.ReturnPointOverride"/>).
///    추락 생존·추락 사망 모두. 연출(낙하 카메라·무적·입력 잠금)은 기존 그대로, 지점만 바뀐다.
///
/// 어느 지점인가: 자동 부활과 같은 '그 플레이어 기준 지금 단계 구간'(<see cref="TutorialSafePointRules.StepArea"/>)의 지점 하나.
/// 주변 적·장애물·바닥은 보지 않는다 — 지점 Transform 위 <see cref="dropHeight"/>(기본 3m)에 내려놓아 떨어져 착지한다.
/// 그 구간에 지점이 없을 때만 기존 동작(안전지점 큐 → 스폰 폴백). 다른 구간 지점으로 대체하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialSafePoints : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public TutorialArea area;
        public Transform point;
    }

    [Tooltip("구간별 고정 안전 지점. 같은 구간이 둘이면 앞 것. 지점이 없는 구간은 기존 추락 복구(최근 밟은 자리)를 쓴다.")]
    [SerializeField] private List<Entry> points = new List<Entry>();

    [Header("낙사")]
    [Tooltip("추락 생존·추락 사망 복귀를 기존 안전지점 큐(최근 밟은 자리) 대신 이 지점으로.")]
    [SerializeField] private bool useOnFall = true;

    [Tooltip("지점 Transform 위로 이만큼(m) 띄워 내려놓는다 — 떨어져 착지. 주변 적·장애물은 보지 않는다.")]
    [SerializeField, Min(0f)] private float dropHeight = TutorialSafePointRules.DefaultDropHeight;

    private Func<ulong, Vector3, Vector3?> _fallOverride;

    public IReadOnlyList<Entry> Points => points;

    public bool TryGetPoint(TutorialArea area, out Transform point)
    {
        foreach (Entry e in points)
        {
            if (e.area != area || e.point == null) continue;
            point = e.point;
            return true;
        }
        point = null;
        return false;
    }

    // ─── 생명주기(서버) ──────────────────────────────────────────────────

    private void OnDisable() => Unhook();

    private void Update()
    {
        if (TryGetServerDirector(out _)) Hook();
        else Unhook();
    }

    private static bool TryGetServerDirector(out TutorialDirector d)
    {
        d = TutorialDirector.Instance;
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsServer && TutorialStageMarker.IsActive && d != null && d.IsTutorialActive;
    }

    private void Hook()
    {
        if (_fallOverride != null) return;

        if (PlayerSafePointTracker.ReturnPointOverride != null)
            Edit.LogWarning("[Tutorial] 추락 복귀 지점 훅이 이미 걸려 있다 — 튜토리얼 안전 지점으로 덮는다.", this);
        _fallOverride = ResolveFallPoint;
        PlayerSafePointTracker.ReturnPointOverride = _fallOverride;

        int count = 0;
        foreach (Entry e in points) if (e.point != null) count++;
        Edit.Log($"[Tutorial] 안전 지점 켜짐 — 지점 {count}개 · 낙사 {(useOnFall ? "켬" : "끔")}.", this);
    }

    private void Unhook()
    {
        if (_fallOverride == null) return;
        if (PlayerSafePointTracker.ReturnPointOverride == _fallOverride)
            PlayerSafePointTracker.ReturnPointOverride = null;
        _fallOverride = null;
    }

    private static bool BossMoveBusy()
    {
        BossTeleportManager t = BossTeleportManager.Instance;
        return t != null && t.IsSpawned && (t.IsCountdownActive || t.IsEncounterBusy);
    }

    // ─── 낙사 ────────────────────────────────────────────────────────────

    private Vector3? ResolveFallPoint(ulong clientId, Vector3 fallPoint)
    {
        if (!useOnFall || !TryGetServerDirector(out TutorialDirector d)) return null;
        if (!TutorialSafePointRules.ShouldWatch(d.IsTutorialActive, d.Step, BossMoveBusy())) return null;

        int mask = 0;
        for (int g = 0; g < TutorialFlowModel.GateCount; g++)
            if (d.IsGateOpen((TutorialGate)g)) mask |= 1 << g;
        TutorialArea stepArea = TutorialSafePointRules.StepArea(d.Step, mask, d.GetBackBarrierMaskServer(clientId));

        if (!TutorialSafePointRules.TryPickStepPoint(stepArea, a => TryGetPoint(a, out _), out TutorialArea area)
            || !TryGetPoint(area, out Transform t))
        {
            Edit.LogWarning($"[Tutorial] client {clientId} 낙사 — {stepArea} 안전 지점이 없다(미배치). 기존 추락 복구로 넘긴다.", this);
            return null;
        }

        Vector3 p = t.position;
        var point = new Vector3(p.x, TutorialSafePointRules.DropY(p.y, dropHeight), p.z);
        Edit.Log($"[Tutorial] client {clientId} 낙사 → {area} 안전 지점 {point}.", this);
        return point;
    }

#if UNITY_EDITOR
    // 지점 확인용 — 초록 원 + 구간 이름.
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        foreach (Entry e in points)
        {
            if (e.point == null) continue;
            Vector3 p = e.point.position;
            Gizmos.DrawWireSphere(p + Vector3.up * 0.5f, 0.5f);
            Gizmos.DrawLine(p, p + Vector3.up * 2f);
            UnityEditor.Handles.Label(p + Vector3.up * 2.2f, $"Safe {e.area}");
        }
    }
#endif
}
