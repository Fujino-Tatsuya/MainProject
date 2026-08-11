using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 피격 이펙트를 <b>어디서 어느 방향으로</b> 재생할지 계산한다. 순수 기하 계산이라
/// <see cref="EffectManager"/>·<see cref="Unit"/> 어느 쪽도 참조하지 않는다.
///
/// <b>세 값이 각각 다른 일을 한다</b>
/// <list type="bullet">
/// <item><c>sourcePosition</c> — 공격자 쪽. <b>방향</b>을 뽑는 데 쓴다. 위치로 쓰면 안 된다:
///       호출 지점에 따라 무기일 수도, <b>플레이어 발밑</b>일 수도 있다</item>
/// <item><c>hitCollider</c> — 피격자 표면. <b>위치</b>를 뽑는다. 없을 수 있다(장판·폭탄)</item>
/// <item><c>fallbackAnchor</c> — 피격자에 미리 꽂아둔 기준점. 계산이 불가능할 때의 <b>보험</b></item>
/// </list>
///
/// 이 프로젝트의 근접 공격은 <c>Physics.Overlap*</c>이라 <b>접촉점이 아예 없다</b> —
/// 여기서 하는 일은 접촉점을 읽는 게 아니라 <b>복원</b>하는 것이다.
/// 투사체(<c>OnTriggerEnter</c>)는 <c>sourcePosition</c>이 곧 접촉점이라 자동으로 다르게 처리된다.
/// </summary>
public static class EffectHitPoint
{
    // 방향·동일점 판정 임계값. 제곱 거리이므로 1e-6 = 1mm.
    private const float MinSqrMagnitude = 1e-6f;

    // 콜라이더 밖으로 확실히 나가기 위한 여유(유닛). 캐릭터 대역이 1.8이므로 1이면 충분하다.
    private const float PushMargin = 1f;

    // ClosestPoint를 지원하지 않는 콜라이더 경고는 대상당 1회. 조용히 틀리는 것을 막되 콘솔은 지킨다.
    private static readonly HashSet<int> Warned = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWarnings() => Warned.Clear();

    public enum HitPointMode
    {
        CameraDir,
        Specific_Position,
        ColliderHit
    }
    public struct HitPointInfo
    {
        public Vector3 sourcePosition;
        public Collider effectCollider;
        public Transform fallbackAnchor;
        public Vector3 facingHint;

        public HitPointInfo(Vector3 _sourcePosition, Collider _collider, Transform _anchor)
        {
            sourcePosition = _sourcePosition;
            effectCollider = _collider;
            fallbackAnchor = _anchor;
            facingHint = default;
        }
    };

    /// <summary>
    /// 타격 지점과 회전을 구한다.
    /// </summary>
    /// <param name="ctx">공격이 넘겨준 기하 정보</param>
    /// <param name="effectCollider">
    /// 피격자의 hurtbox 콜라이더 대신 전용 effect 콜라이더에서 이펙트를 재생하고 싶을 때 쓴다.
    /// </param>
    /// <param name="fallbackAnchor">
    /// 피격자의 기준점(가슴 높이 등). <c>hitCollider</c>가 없을 때 쓴다. null이면 <c>sourcePosition</c>으로 퇴화한다
    /// </param>
    /// <param name="facingHint">
    /// 방향을 직접 아는 호출자용(예: 투사체 진행 방향의 반대). 0이면 무시하고 아래 순서로 추론한다
    /// </param>
    public static Pose Resolve(HitPointMode mode, HitPointInfo hitInfo)
    {
        Vector3 origin = hitInfo.sourcePosition;
        Collider collider = hitInfo.effectCollider;

        Vector3 point = origin;
        Vector3 center = origin;

        switch (mode)
        {
            case HitPointMode.CameraDir:
            {
                Bounds bounds = collider.bounds;
                center = bounds.center;
                point = SurfacePoint(collider, bounds, Camera.main.transform.position);
                hitInfo.facingHint = Vector3.Normalize(collider.transform.position - Camera.main.transform.position);
                break;
            }
            case HitPointMode.Specific_Position:
            {
                point = hitInfo.fallbackAnchor.position;
                center = point;
                break;
            }
            case HitPointMode.ColliderHit:
            {
                Bounds bounds = collider.bounds;
                center = bounds.center;
                point = SurfacePoint(collider, bounds, origin);
                break;
            }
        }

        return new Pose(point, Facing(origin, point, center, hitInfo.facingHint));
    }

    /// <summary>콜라이더 표면에서 <paramref name="origin"/>에 가장 가까운 점.</summary>
    private static Vector3 SurfacePoint(Collider collider, Bounds bounds, Vector3 origin)
    {
        // origin이 콜라이더 밖이면 이 값이 가장 정확하다.
        Vector3 point = collider.ClosestPoint(origin);
        if ((point - origin).sqrMagnitude > MinSqrMagnitude) return point;

        // 입력이 그대로 돌아왔다 = origin이 콜라이더 '안'이었거나(근접 무기가 파고든 경우)
        // ClosestPoint를 지원하지 않는 콜라이더다. 확실히 바깥인 점을 만들어 다시 묻는다.
        Vector3 toOrigin = origin - bounds.center;
        Vector3 outward = toOrigin.sqrMagnitude > MinSqrMagnitude ? toOrigin.normalized : Vector3.up;

        // extents.magnitude = AABB 중심에서 모서리까지의 거리. 콜라이더 위의 어떤 점도 이보다 멀 수 없다.
        Vector3 outside = bounds.center + outward * (bounds.extents.magnitude + PushMargin);

        Vector3 pushed = collider.ClosestPoint(outside);
        if ((pushed - outside).sqrMagnitude > MinSqrMagnitude) return pushed;

        // 바깥에서 물었는데도 입력이 그대로다 = ClosestPoint를 지원하지 않는 콜라이더.
        // (Box·Sphere·Capsule·convex Mesh만 지원한다. 비볼록 MeshCollider가 여기 걸린다.)
        WarnUnsupported(collider);
        return bounds.ClosestPoint(outside);   // AABB 표면으로라도 근사한다
    }

    /// <summary>
    /// 이펙트가 바라볼 방향. <see cref="Quaternion.LookRotation"/>은 <b>로컬 +Z</b>를 이 방향에 맞춘다 —
    /// 파티클 프리팹이 +Z로 방출한다는 전제다(유니티 기본값).
    /// </summary>
    private static Quaternion Facing(Vector3 origin, Vector3 point, Vector3 center,
                                     Vector3 hint)
    {
        // ① 호출자가 방향을 알고 있으면 그대로 쓴다
        if (hint.sqrMagnitude > MinSqrMagnitude) return Quaternion.LookRotation(hint);

        // ② 공격자 쪽으로 튀어나가게 — 근접의 정상 경로
        Vector3 toAttacker = origin - point;
        if (toAttacker.sqrMagnitude > MinSqrMagnitude) return Quaternion.LookRotation(toAttacker);

        // ③ 투사체는 sourcePosition이 곧 접촉점이라 ②가 0이 된다 → 피격자 중심에서 바깥으로
        Vector3 outward = point - center;
        if (outward.sqrMagnitude > MinSqrMagnitude) return Quaternion.LookRotation(outward);

        return Quaternion.identity;
    }

    private static void WarnUnsupported(Collider collider)
    {
        if (!Warned.Add(collider.GetInstanceID())) return;

        Edit.LogWarning(
            $"[Effect] '{collider.name}'({collider.GetType().Name})는 ClosestPoint를 지원하지 않는다. " +
            "Box·Sphere·Capsule·convex MeshCollider만 지원하며, 그 외에는 입력을 그대로 돌려준다(에러 없음). " +
            "타격 지점을 AABB 표면으로 근사했다 — 정확한 위치가 필요하면 콜라이더를 바꿀 것.", collider);
    }
}
