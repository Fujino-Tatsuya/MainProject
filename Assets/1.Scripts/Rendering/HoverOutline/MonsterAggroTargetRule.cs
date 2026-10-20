using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// "나를 노리는 몬스터" 회색 외곽선(할 일 58)의 규칙 — 순수 함수, 할당 없음.
/// <list type="bullet">
/// <item><b>서버 쓰기</b>(<see cref="ReplicatedTargetId"/>): <see cref="MonsterBase"/> 가 복제하는 대상 id.
///   Chase·Attack 이고 살아 있고 서버 로직이 돌 때(휴면·연출 정지 아님)만 현재 대상의 NetworkObjectId, 그 밖 0.</item>
/// <item><b>클라 읽기</b>(<see cref="IsTargetingLocal"/>): 복제 id == 로컬 플레이어 id && 고를 수 있음(살아 있음).</item>
/// <item><b>고르기</b>(<see cref="SelectAggro"/>): 빨강(호버·스킬 범위) 대상은 뺀다(빨강 우선 — 한 몬스터는 한 색), 가까운 순, 상한.
///   스킬 인디케이터가 켜져 있으면 전부 끈다(<see cref="FrameStamp"/>).</item>
/// </list>
/// NetworkObjectId 0 은 NGO 가 발급하지 않는다(카운터를 먼저 올린다) — 0 = 대상 없음.
/// </summary>
public static class MonsterAggroTargetRule
{
    public const ulong NoTarget = 0;
    public const int DefaultMaxTargets = 8;

    /// <summary>[서버] 복제할 대상 id.</summary>
    public static ulong ReplicatedTargetId(MonsterState state, bool dead, bool logicStopped, ulong targetId)
    {
        if (dead || logicStopped)
            return NoTarget;
        if (state != MonsterState.Chase && state != MonsterState.Attack)
            return NoTarget;
        return targetId;
    }

    /// <summary>[클라] 이 몬스터가 나를 노리는가.</summary>
    public static bool IsTargetingLocal(ulong replicatedTargetId, ulong localPlayerId, bool selectable)
    {
        return selectable && replicatedTargetId != NoTarget && replicatedTargetId == localPlayerId;
    }

    /// <summary>
    /// 나를 노리는 후보에서 나를 노림 세트를 고른다. <paramref name="hovered"/>·<paramref name="primaryTargets"/>(빨강)에 든 대상은 뺀다.
    /// 후보의 Selectable 은 <see cref="IsTargetingLocal"/> 결과여야 한다.
    /// <paramref name="skillIndicatorActive"/> 면 비운다 — 스킬 인디케이터가 떠 있는 동안은 빨강(호버·범위)만 보인다.
    /// 대상 유무가 아니라 표시 유무 기준이다(범위 대상 0 이어도 끈다).
    /// </summary>
    public static void SelectAggro<T>(List<OutlineCandidate<T>> candidates, T hovered, IReadOnlyList<T> primaryTargets,
        int maxTargets, List<T> result, bool skillIndicatorActive = false) where T : class
    {
        if (skillIndicatorActive)
        {
            result.Clear();
            return;
        }

        if (candidates != null && primaryTargets != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                OutlineCandidate<T> c = candidates[i];
                if (c.Selectable && Contains(primaryTargets, c.Target))
                {
                    c.Selectable = false;
                    candidates[i] = c;
                }
            }
        }

        SkillRangeOutlineSelection.Select(candidates, hovered, maxTargets, result);
    }

    static bool Contains<T>(IReadOnlyList<T> list, T target) where T : class
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], target))
                return true;
        return false;
    }

    /// <summary>
    /// [오너 로컬] "스킬 인디케이터 켜짐" 프레임 도장. 표시를 그리는 쪽이 매 프레임 찍고, 안 찍은 프레임엔 꺼진 것으로 본다
    /// (보고가 끊기면 자동 해제). 표시가 꺼지는 프레임엔 <see cref="Clear"/> 로 같은 프레임에 푼다.
    /// </summary>
    public struct FrameStamp
    {
        int _frame;

        public void Mark(int frame) => _frame = frame + 1; // 0 = 찍은 적 없음(기본값)
        public void Clear() => _frame = 0;
        public bool IsSet(int frame) => _frame == frame + 1;
    }

    /// <summary>
    /// [서버] 대상 Transform → NetworkObjectId 캐시. 대상이 바뀐 틱에만 <c>GetComponentInParent</c> 를 한다.
    /// 대상이 NetworkObject 아래가 아니면 0.
    /// </summary>
    public struct TargetIdCache
    {
        Transform _source;
        ulong _id;

        public ulong Resolve(Transform target)
        {
            if (target == null)
            {
                _source = null;
                _id = NoTarget;
                return NoTarget;
            }

            if (target != _source)
            {
                _source = target;
                NetworkObject no = target.GetComponentInParent<NetworkObject>();
                _id = no != null && no.IsSpawned ? no.NetworkObjectId : NoTarget;
            }
            return _id;
        }
    }
}
