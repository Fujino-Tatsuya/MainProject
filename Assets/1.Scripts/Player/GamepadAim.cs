using System.Collections.Generic;
using UnityEngine;

/// <summary>패드 조준 후보 하나 — 대상·월드 위치·고를 수 있는가(<see cref="CursorUnitPick.IsSelectable(Unit, Unit)"/>).</summary>
public readonly struct GamepadAimCandidate<T>
{
    public readonly T Item;
    public readonly Vector3 Position;
    public readonly bool Selectable;

    public GamepadAimCandidate(T item, Vector3 position, bool selectable)
    {
        Item = item;
        Position = position;
        Selectable = selectable;
    }
}

/// <summary>
/// 패드 조준 혼합 순수 규칙(할 일 278, Docs/tech/gamepad-support.md §3).
/// 오른쪽 스틱이 데드존을 넘으면 그 방향, 놓으면 기준 방향(이동 중 = 이동 방향, 정지 = 마지막 조준) 원뿔 안 적에게 자동 보정.
/// </summary>
public static class GamepadAimRules
{
    public static bool IsStickActive(Vector2 stick, float deadzone) =>
        stick.sqrMagnitude > 0f && stick.sqrMagnitude >= deadzone * deadzone;

    /// <summary>스틱(화면 기준 x=오른쪽, y=위)을 이동과 같은 뷰 yaw 로 월드 평면 방향으로. 0 이면 zero.</summary>
    public static Vector3 StickToWorld(Vector2 stick, float viewYaw)
    {
        Vector3 world = Quaternion.Euler(0f, viewYaw, 0f) * new Vector3(stick.x, 0f, stick.y);
        world.y = 0f;
        return world.sqrMagnitude > 0.0001f ? world.normalized : Vector3.zero;
    }

    /// <summary>스틱을 놓았을 때 기준 방향 — 이동 방향 → 마지막 조준 → 캐릭터 정면 순.</summary>
    public static Vector3 BaseDirection(Vector3 moveWorld, Vector3 lastAim, Vector3 facing)
    {
        if (TryFlat(moveWorld, out Vector3 move))
            return move;
        if (TryFlat(lastAim, out Vector3 last))
            return last;
        return TryFlat(facing, out Vector3 face) ? face : Vector3.forward;
    }

    /// <summary>
    /// 보정 점수 — 원뿔(반각 <paramref name="halfAngleDeg"/>)·거리(<paramref name="range"/>) 안이면 참이고 점수 = 각도 비율 + 거리 비율(낮을수록 좋다).
    /// 원점과 겹친 대상(수평 거리 0)은 각도 0 으로 본다.
    /// </summary>
    public static bool TryScore(Vector3 origin, Vector3 direction, Vector3 position, float range, float halfAngleDeg, out float score)
    {
        score = float.MaxValue;
        Vector3 offset = position - origin;
        offset.y = 0f;
        float distance = offset.magnitude;
        if (range <= 0f || distance > range)
            return false;

        float angle = distance > 0.0001f && TryFlat(direction, out Vector3 dir) ? Vector3.Angle(dir, offset) : 0f;
        if (angle > halfAngleDeg)
            return false;

        score = (halfAngleDeg > 0f ? angle / halfAngleDeg : 0f) + distance / range;
        return true;
    }

    /// <summary>보정 대상 — 고를 수 있고 원뿔·거리 안인 후보 중 점수가 가장 낮은 것의 인덱스(같으면 앞). 없으면 -1.</summary>
    public static int SelectAssist<T>(IReadOnlyList<GamepadAimCandidate<T>> candidates, Vector3 origin, Vector3 direction,
        float range, float halfAngleDeg)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        if (candidates == null)
            return best;

        for (int i = 0; i < candidates.Count; i++)
        {
            GamepadAimCandidate<T> c = candidates[i];
            if (!c.Selectable || !TryScore(origin, direction, c.Position, range, halfAngleDeg, out float score) || score >= bestScore)
                continue;
            best = i;
            bestScore = score;
        }

        return best;
    }

    public static bool TryFlat(Vector3 v, out Vector3 flat)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f)
        {
            flat = Vector3.zero;
            return false;
        }

        flat = v.normalized;
        return true;
    }
}

/// <summary>
/// 단일 대상 스킬 패드 후보 순환 순수 규칙(할 일 278, Docs/tech/gamepad-support.md §4).
/// 원뿔 안 후보를 왼→오른쪽 각도 순으로 세우고, 처음 = 가장 가까운 것, 십자키·스틱 튕김으로 이전/다음(끝에서 돌아감).
/// </summary>
public static class GamepadTargetCycle
{
    public const float FlickFireThreshold = 0.6f;
    public const float FlickRearmThreshold = 0.3f;

    /// <summary><paramref name="current"/> 에서 <paramref name="delta"/> 칸 이동(끝에서 돌아감). 후보 없음 = -1, 선택 없음(-1) = 0 에서 시작.</summary>
    public static int Step(int count, int current, int delta)
    {
        if (count <= 0)
            return -1;
        if (current < 0 || current >= count)
            return 0;
        return ((current + delta) % count + count) % count;
    }

    /// <summary>
    /// 스틱 튕김 엣지 — 가로 값이 <paramref name="fire"/> 를 넘는 순간 한 번 ±1, 다시 쏘려면 <paramref name="rearm"/> 아래로 돌아와야 한다.
    /// <paramref name="armed"/> 는 처음 true.
    /// </summary>
    public static int FlickStep(ref bool armed, float x, float fire = FlickFireThreshold, float rearm = FlickRearmThreshold)
    {
        float abs = Mathf.Abs(x);
        if (armed)
        {
            if (abs < fire)
                return 0;
            armed = false;
            return x > 0f ? 1 : -1;
        }

        if (abs < rearm)
            armed = true;
        return 0;
    }

    /// <summary>
    /// 원뿔(축 <paramref name="axis"/>, 반각)·거리 안 고를 수 있는 후보를 왼→오른쪽(위에서 본 시계 방향 각도 오름차순)으로 <paramref name="into"/> 에 담는다.
    /// </summary>
    public static void BuildOrder<T>(IReadOnlyList<GamepadAimCandidate<T>> candidates, Vector3 origin, Vector3 axis,
        float range, float halfAngleDeg, List<GamepadAimCandidate<T>> into)
    {
        into.Clear();
        if (candidates == null)
            return;

        for (int i = 0; i < candidates.Count; i++)
        {
            GamepadAimCandidate<T> c = candidates[i];
            if (c.Selectable && GamepadAimRules.TryScore(origin, axis, c.Position, range, halfAngleDeg, out _))
                into.Add(c);
        }

        GamepadAimRules.TryFlat(axis, out Vector3 flatAxis);
        into.Sort((a, b) => SignedAngle(origin, flatAxis, a.Position).CompareTo(SignedAngle(origin, flatAxis, b.Position)));
    }

    /// <summary>가장 가까운(수평 거리) 후보 인덱스. 없으면 -1.</summary>
    public static int NearestIndex<T>(IReadOnlyList<GamepadAimCandidate<T>> ordered, Vector3 origin)
    {
        int best = -1;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < ordered.Count; i++)
        {
            Vector3 offset = ordered[i].Position - origin;
            offset.y = 0f;
            float sqr = offset.sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            best = i;
            bestSqr = sqr;
        }

        return best;
    }

    /// <summary>후보 목록이 다시 만들어진 뒤 고른 것 유지 — 아직 있으면 그 인덱스, 없으면 가장 가까운 것.</summary>
    public static int Reselect<T>(IReadOnlyList<GamepadAimCandidate<T>> ordered, Vector3 origin, T previous, bool hasPrevious)
    {
        if (hasPrevious)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (comparer.Equals(ordered[i].Item, previous))
                    return i;
            }
        }

        return NearestIndex(ordered, origin);
    }

    static float SignedAngle(Vector3 origin, Vector3 flatAxis, Vector3 position)
    {
        Vector3 offset = position - origin;
        offset.y = 0f;
        return offset.sqrMagnitude < 0.0001f ? 0f : Vector3.SignedAngle(flatAxis, offset, Vector3.up);
    }
}

/// <summary>패드 조준 후보 수집(씬 질의) — 반경 안 고를 수 있는 유닛을 하나씩(콜라이더 여럿이어도 한 번).</summary>
public static class GamepadAimQuery
{
    const int BufferSize = 64;
    static readonly Collider[] s_hits = new Collider[BufferSize];
    static readonly HashSet<Unit> s_seen = new HashSet<Unit>();

    public static void CollectUnits(Vector3 origin, float radius, LayerMask mask, Unit self, List<GamepadAimCandidate<Unit>> into)
    {
        into.Clear();
        if (radius <= 0f || mask == 0)
            return;

        s_seen.Clear();
        int count = Physics.OverlapSphereNonAlloc(origin, radius, s_hits, mask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider col = s_hits[i];
            s_hits[i] = null;
            Unit unit = CursorUnitPick.ResolveUnit(col);
            if (unit == null || !s_seen.Add(unit))
                continue;

            into.Add(new GamepadAimCandidate<Unit>(unit, unit.transform.position, CursorUnitPick.IsSelectable(unit, self)));
        }

        s_seen.Clear();
    }
}
