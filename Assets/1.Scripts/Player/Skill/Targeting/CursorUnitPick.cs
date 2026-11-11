using System.Collections.Generic;
using UnityEngine;

/// <summary>커서 레이 한 발의 판정 결과. 커서 모양(조준 중/무효 대상)을 가르는 데 쓴다.</summary>
public enum CursorPickResult
{
    NoHit,          // 마스크 안의 콜라이더에 안 맞았다
    InvalidTarget,  // 맞았지만 고를 수 없는 대상(유닛 아님·자기 자신·HP 0)
    ValidTarget,
}

/// <summary>널널한 판정 후보 — 커서 바닥 지점 반경 안의 유닛 하나(<see cref="CursorUnitPick.Resolve{T}"/>).</summary>
public readonly struct CursorPickCandidate<T>
{
    public readonly T Item;
    public readonly float FlatDistance;  // 커서 바닥 지점 ↔ 유닛 콜라이더 표면의 수평 거리(m)
    public readonly bool Selectable;     // CursorUnitPick.IsSelectable 결과

    public CursorPickCandidate(T item, float flatDistance, bool selectable)
    {
        Item = item;
        FlatDistance = flatDistance;
        Selectable = selectable;
    }
}

/// <summary>
/// 커서 아래 유닛 판정 한 곳 — 스킬 조준(<see cref="PlayerSkillTargeting"/> SingleTarget)과
/// 몬스터 호버 외곽선(<see cref="PlayerEnemyHoverOutline"/>)이 같은 규칙을 쓴다. 외곽선이 뜬 유닛 = 확정 시 시전 대상.
/// 레이 → 마스크 안 첫 콜라이더 → <see cref="ResolveUnit"/>(Hurtbox 우선) → 자기 자신·HP 0 제외.
/// 널널한 판정(할 일 265): 레이가 고를 수 있는 유닛에 안 맞으면 커서가 가리키는 바닥 지점에서 수평 반경
/// (<see cref="PlayerInputReader.CursorPickRadius"/>, 0 = 끔) 안의 고를 수 있는 유닛 중 콜라이더 표면이 가장 가까운 것을 잡는다.
/// 서버 검증(사거리·대상 유효성)은 그대로다 — 이건 오너 로컬 대상 고르기만 바꾼다.
/// </summary>
public static class CursorUnitPick
{
    public const float RaycastMaxDistance = 200f;

    // 반경 겹침을 바닥 지점 위아래로 이만큼 세운 캡슐로 한다(유닛 키·단차를 덮는 높이).
    const float LenientProbeHalfHeight = 3f;
    const int LenientBufferSize = 64;
    static readonly Collider[] s_lenientHits = new Collider[LenientBufferSize];
    static readonly List<CursorPickCandidate<Unit>> s_lenientCandidates = new List<CursorPickCandidate<Unit>>(LenientBufferSize);

    /// <summary>예전 판정(반경 0) — 레이가 정확히 맞은 유닛만.</summary>
    public static CursorPickResult Pick(Ray ray, LayerMask mask, Unit self, out Unit unit)
    {
        unit = null;
        if (!Physics.Raycast(ray, out RaycastHit hit, RaycastMaxDistance, mask, QueryTriggerInteraction.Collide))
            return CursorPickResult.NoHit;

        Unit resolved = ResolveUnit(hit.collider);
        if (!IsSelectable(resolved, self))
            return CursorPickResult.InvalidTarget;

        unit = resolved;
        return CursorPickResult.ValidTarget;
    }

    /// <summary>
    /// 널널한 판정 — 직접 맞으면 그 유닛, 아니면 바닥 지점(<paramref name="groundMask"/> 레이, 없으면 자기 발 높이 수평면)
    /// 수평 <paramref name="lenientRadius"/> 안의 고를 수 있는 유닛 중 가장 가까운 것. 반경 0 이하 = <see cref="Pick(Ray, LayerMask, Unit, out Unit)"/>.
    /// </summary>
    public static CursorPickResult Pick(Ray ray, LayerMask mask, Unit self, LayerMask groundMask, float lenientRadius, out Unit unit)
    {
        CursorPickResult direct = Pick(ray, mask, self, out Unit directUnit);
        if (direct == CursorPickResult.ValidTarget || lenientRadius <= 0f)
        {
            unit = directUnit;
#if UNITY_EDITOR
            bool hasPointForTrace = TryGetGroundPoint(ray, groundMask, self, out Vector3 tracePoint);
            Record(hasPointForTrace, tracePoint, lenientRadius, direct, unit, direct == CursorPickResult.ValidTarget);
#endif
            return direct;
        }

        s_lenientCandidates.Clear();
        bool hasPoint = TryGetGroundPoint(ray, groundMask, self, out Vector3 point);
        if (hasPoint)
            CollectNearby(point, lenientRadius, mask, self, s_lenientCandidates);

        CursorPickResult result = Resolve(direct, directUnit, s_lenientCandidates, lenientRadius, out unit);
        s_lenientCandidates.Clear();
#if UNITY_EDITOR
        Record(hasPoint, point, lenientRadius, result, unit, false);
#endif
        return result;
    }

#if UNITY_EDITOR
    /// <summary>
    /// [에디터 전용·디버그] 널널한 판정 한 번의 결과 — 특성 랩 커서 범위 기즈모(<c>TraitLabCursorPickGizmo</c>)가 읽는다.
    /// 판정을 다시 하지 않고 조준·호버 외곽선이 실제로 부른 그 결과를 그대로 보여 주려고 남긴다.
    /// </summary>
    public readonly struct Trace
    {
        public readonly int Frame;
        public readonly bool HasGroundPoint;
        public readonly Vector3 GroundPoint;
        public readonly float Radius;
        public readonly CursorPickResult Result;
        public readonly Unit Unit;
        public readonly bool DirectHit;   // true = 광선이 직접 맞음, false 이고 Unit 있음 = 반경으로 잡음

        public Trace(int frame, bool hasGroundPoint, Vector3 groundPoint, float radius, CursorPickResult result, Unit unit, bool directHit)
        {
            Frame = frame;
            HasGroundPoint = hasGroundPoint;
            GroundPoint = groundPoint;
            Radius = radius;
            Result = result;
            Unit = unit;
            DirectHit = directHit;
        }
    }

    /// <summary>[에디터 전용] 마지막 널널한 판정. Frame 이 오래됐으면 이번 프레임엔 판정이 안 돌았다(오너 아님·카메라 없음 등).</summary>
    public static Trace LastTrace { get; private set; }

    static void Record(bool hasPoint, Vector3 point, float radius, CursorPickResult result, Unit unit, bool directHit) =>
        LastTrace = new Trace(Time.frameCount, hasPoint, point, radius, result, unit, directHit);
#endif

    /// <summary>
    /// 순수 규칙 — 직접 맞음(ValidTarget)이 우선, 반경 0 이하면 직접 결과 그대로(예전 동작).
    /// 아니면 반경 안(거리 ≤ 반경) 고를 수 있는 후보 중 가장 가까운 것(같으면 앞 후보). 없으면 직접 결과(NoHit/InvalidTarget) 그대로.
    /// </summary>
    public static CursorPickResult Resolve<T>(CursorPickResult direct, T directItem, IReadOnlyList<CursorPickCandidate<T>> nearby,
        float radius, out T picked)
    {
        if (direct == CursorPickResult.ValidTarget || radius <= 0f || nearby == null)
        {
            picked = direct == CursorPickResult.ValidTarget ? directItem : default;
            return direct;
        }

        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < nearby.Count; i++)
        {
            CursorPickCandidate<T> c = nearby[i];
            if (!c.Selectable || c.FlatDistance > radius || c.FlatDistance >= bestDistance)
                continue;
            best = i;
            bestDistance = c.FlatDistance;
        }

        if (best < 0)
        {
            picked = default;
            return direct;
        }

        picked = nearby[best].Item;
        return CursorPickResult.ValidTarget;
    }

    public static bool IsSelectable(Unit unit, Unit self)
    {
        return unit != null && IsSelectable(unit == self, unit.CurrentHealth);
    }

    /// <summary>순수 규칙 — 자기 자신이 아니고 살아 있어야(HP &gt; 0) 고를 수 있다. 사망 처리 중인 몬스터는 HP 0 이다.</summary>
    public static bool IsSelectable(bool isSelf, int currentHealth)
    {
        return !isSelf && currentHealth > 0;
    }

    // 레이캐스트 히트 → Unit 해석 (Hurtbox 우선, 없으면 상위 Unit). FirstMeleeMainSkill.ResolveUnit 패턴과 동일.
    public static Unit ResolveUnit(Collider hit)
    {
        if (hit == null)
            return null;

        Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
        if (hurtbox != null && hurtbox.TryGetOwner(out Unit hurtboxOwner))
            return hurtboxOwner;

        return hit.GetComponentInParent<Unit>();
    }

    // PlayerAimIndicator 조준점과 같은 규칙 — 바닥 레이, 빗나가면 자기 발 높이 수평면.
    static bool TryGetGroundPoint(Ray ray, LayerMask groundMask, Unit self, out Vector3 point)
    {
        if (groundMask != 0 && Physics.Raycast(ray, out RaycastHit hit, RaycastMaxDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        Vector3 planePoint = self != null ? self.transform.position : Vector3.zero;
        if (new Plane(Vector3.up, planePoint).Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        point = default;
        return false;
    }

    static void CollectNearby(Vector3 point, float radius, LayerMask mask, Unit self, List<CursorPickCandidate<Unit>> into)
    {
        Vector3 up = Vector3.up * LenientProbeHalfHeight;
        int count = Physics.OverlapCapsuleNonAlloc(point - up, point + up, radius, s_lenientHits, mask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider col = s_lenientHits[i];
            s_lenientHits[i] = null;
            Unit unit = ResolveUnit(col);
            if (unit == null)
                continue;

            into.Add(new CursorPickCandidate<Unit>(unit, FlatDistanceToSurface(col, point), IsSelectable(unit, self)));
        }
    }

    // 큰 몸(보스)도 중심이 아니라 표면까지 잰다 — 안에 들어가 있으면 0.
    static float FlatDistanceToSurface(Collider col, Vector3 point)
    {
        Bounds b = col.bounds;
        Vector3 probe = new Vector3(point.x, Mathf.Clamp(point.y, b.min.y, b.max.y), point.z);
        bool supportsClosest = !(col is MeshCollider mesh) || mesh.convex;
        Vector3 closest = supportsClosest && !(col is TerrainCollider) ? col.ClosestPoint(probe) : b.ClosestPoint(probe);
        float dx = closest.x - point.x;
        float dz = closest.z - point.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
