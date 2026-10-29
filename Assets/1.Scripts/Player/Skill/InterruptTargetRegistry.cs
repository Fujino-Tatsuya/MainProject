using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 간파 예측 대상 캐시(할 일 132 정정1) — 간파 시도 예측(서버 승인·오너 입력 순간)은 누를 때마다 오버랩하지 않고 여기 등록된 대상의
/// 거리·각도만 계산한다(<see cref="BeaverLobby.Player.Dash.InterruptTargetGeometry"/>). 서버·클라 모두 같은 규칙으로 채운다.
/// <list type="bullet">
/// <item>등록 = 대상 스폰(OnNetworkSpawn, 모든 피어). 23호는 보스전 시작(BossEncounterDirector 가 Spawn)이 곧 등록이다.
/// 중간보스·스파링 봇 = <see cref="MonsterBase"/>, 허수아비 = <see cref="TrainingDummyInterrupt"/>.</item>
/// <item>해제 = 디스폰(OnNetworkDespawn — 보스전 끝·씬 전환 디스폰 포함)·사망(MonsterBase Dead 상태, 모든 피어)·씬 언로드(남은 것 정리).</item>
/// </list>
/// 대상 = <see cref="IInterruptSlowMotionTarget"/> 를 가진 Unit 만(창 소유자). 실제 판정(ResolveHit 오버랩)은 이 캐시를 쓰지 않는다.
/// </summary>
public static class InterruptTargetRegistry
{
    /// <summary>등록된 간파 대상 하나 — 스폰 때 콜라이더를 모아 둔다(거리 = 판정 레이어 콜라이더 표면까지).</summary>
    public sealed class Entry
    {
        public readonly Unit Unit;
        public readonly IInterruptSlowMotionTarget Target;
        /// <summary>창 복제값·정면 허용각. 구현 안 한 대상은 null(오너 예측 = 창 닫힘, 정면 판정 없음).</summary>
        public readonly IInterruptWindowReplica Replica;
        private readonly Collider[] _colliders;

        internal Entry(Unit unit, IInterruptSlowMotionTarget target)
        {
            Unit = unit;
            Target = target;
            Replica = target as IInterruptWindowReplica ?? unit.GetComponent<IInterruptWindowReplica>();
            _colliders = unit.GetComponentsInChildren<Collider>(true);
        }

        /// <summary>정면 허용 반각(도) — 판정과 같은 값. 복제 인터페이스가 없으면 정면 판정 없음(180).</summary>
        public float FrontAngle => Replica != null ? Replica.InterruptFrontAngle : BeaverLobby.Player.Dash.InterruptTargetGeometry.NoFrontRule;

        /// <summary>
        /// <paramref name="pivot"/> → 판정 레이어(<paramref name="layers"/>) 안 켜진 콜라이더 표면까지 최소 수평 거리.
        /// 그런 콜라이더가 없으면 false(판정 오버랩에도 안 걸리는 대상).
        /// </summary>
        public bool TryGetHorizontalDistance(Vector3 pivot, LayerMask layers, out float distance)
        {
            distance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider c = _colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || (layers.value & (1 << c.gameObject.layer)) == 0)
                    continue;

                found = true;
                distance = Mathf.Min(distance, InterruptOmniRangeModel.HorizontalDistanceToBounds(pivot, c.bounds));
            }

            return found;
        }
    }

    private static readonly List<Entry> s_entries = new List<Entry>();

    /// <summary>등록 수(파괴된 것 포함 — 조회 때 정리된다).</summary>
    public static int Count => s_entries.Count;

    /// <summary>
    /// [모든 피어] 대상 등록. <see cref="IInterruptSlowMotionTarget"/> 가 없는 Unit 은 무시(false). 이미 있으면 그대로(false).
    /// </summary>
    public static bool Register(Unit unit)
    {
        if (unit == null || IndexOf(unit) >= 0 || !unit.TryGetComponent(out IInterruptSlowMotionTarget target))
            return false;

        s_entries.Add(new Entry(unit, target));
        return true;
    }

    /// <summary>[모든 피어] 대상 해제(디스폰·사망). 없으면 false.</summary>
    public static bool Unregister(Unit unit)
    {
        int index = IndexOf(unit);
        if (index < 0)
            return false;

        s_entries.RemoveAt(index);
        return true;
    }

    public static bool Contains(Unit unit) => IndexOf(unit) >= 0;

    public static void Clear() => s_entries.Clear();

    /// <summary>파괴된 대상과 <paramref name="scene"/> 에 속한 대상을 뺀다(씬 언로드). 뺀 수.</summary>
    public static int PruneScene(Scene scene)
    {
        int removed = 0;
        for (int i = s_entries.Count - 1; i >= 0; i--)
        {
            Unit unit = s_entries[i].Unit;
            if (unit != null && unit.gameObject.scene != scene)
                continue;

            s_entries.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    /// <summary>
    /// 살아 있는 등록 대상을 <paramref name="results"/> 에 채운다(파괴된 것은 이때 정리). 예측은 이 목록으로 거리·각도만 계산한다.
    /// </summary>
    public static void CopyLiveTo(List<Entry> results)
    {
        results.Clear();
        for (int i = s_entries.Count - 1; i >= 0; i--)
        {
            if (s_entries[i].Unit == null)
            {
                s_entries.RemoveAt(i);
                continue;
            }

            results.Add(s_entries[i]);
        }
    }

    private static int IndexOf(Unit unit)
    {
        if (unit == null)
            return -1;

        for (int i = 0; i < s_entries.Count; i++)
            if (ReferenceEquals(s_entries[i].Unit, unit))
                return i;
        return -1;
    }

    private static void OnSceneUnloaded(Scene scene) => PruneScene(scene);

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 대상이 남지 않게 비우고, 씬 언로드 정리를 한 번만 구독한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        s_entries.Clear();
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }
}
