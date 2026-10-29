using UnityEngine;

/// <summary>
/// 어쌔신 평타 판정 상자(할 일 144) — 프리팹 <c>Armature/DefaultAttack/&lt;타&gt;</c> 의 BoxCollider(<see cref="ColliderInfo"/>)를
/// 이 타의 조준 방향(<c>attackDirection</c>) 기준으로 시전자 발밑에 다시 놓는다. 가붕이 평타(AAC1~4)처럼 콜라이더를 사용자가 세팅하되,
/// 판정 방향은 몸(Armature) 회전이 아니라 지금처럼 그 타의 조준 방향이다 — 서버가 원격 오너의 몸 회전을 늦게 받아도 방향이 같다.
/// 특성 범위 배율(칼날의 영역)은 상자 길이(z)·폭(x)에 곱한다 — 몸 기준점(원점)에서 늘어나므로 상자 중심도 같은 배율로 옮겨진다.
/// </summary>
public readonly struct AssassinStrikeBox
{
    public readonly Vector3 Center;
    public readonly Vector3 HalfExtents;
    public readonly Quaternion Rotation;

    public AssassinStrikeBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        Center = center;
        HalfExtents = halfExtents;
        Rotation = rotation;
    }

    /// <summary>
    /// <paramref name="anchorToFrame"/> = 앵커 로컬 → 캐릭터 정면 기준 프레임(Armature 로컬) 변환. 앵커 노드의 위치·회전·스케일을 그대로 싣는다.
    /// <paramref name="lengthMultiplier"/>·<paramref name="widthMultiplier"/> = 프레임 z·x 축 배율(1 = 그대로, 음수 = 0).
    /// </summary>
    public static AssassinStrikeBox Place(Vector3 origin, Vector3 forward, Matrix4x4 anchorToFrame,
        Vector3 boxCenter, Vector3 boxSize, float lengthMultiplier = 1f, float widthMultiplier = 1f)
    {
        forward.y = 0f;
        Quaternion facing = forward.sqrMagnitude >= 0.0001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.identity;

        Vector3 traitScale = new Vector3(Mathf.Max(0f, widthMultiplier), 1f, Mathf.Max(0f, lengthMultiplier));
        Vector3 localCenter = Vector3.Scale(anchorToFrame.MultiplyPoint3x4(boxCenter), traitScale);
        Vector3 anchorScale = anchorToFrame.lossyScale;
        Vector3 half = Vector3.Scale(new Vector3(Mathf.Abs(boxSize.x), Mathf.Abs(boxSize.y), Mathf.Abs(boxSize.z)) * 0.5f,
            new Vector3(Mathf.Abs(anchorScale.x), Mathf.Abs(anchorScale.y), Mathf.Abs(anchorScale.z)));
        // 앵커를 회전해 두지 않은 기본(정면 정렬) 세팅에선 상자 축 = 프레임 축이라 배율이 정확하다.
        half = Vector3.Scale(half, traitScale);

        return new AssassinStrikeBox(origin + facing * localCenter, half, facing * anchorToFrame.rotation);
    }

    /// <summary>앵커 노드의 BoxCollider 로 <see cref="Place"/>. 앵커가 없거나 BoxCollider 가 아니면 false.</summary>
    public static bool TryPlace(ColliderInfo anchor, Transform owner, Vector3 forward,
        float lengthMultiplier, float widthMultiplier, out AssassinStrikeBox box)
    {
        box = default;
        if (anchor == null || owner == null || !anchor.TryGetLocalBox(out Vector3 center, out Vector3 size))
            return false;

        Transform frame = FrameOf(anchor.transform, owner);
        Matrix4x4 anchorToFrame = frame.worldToLocalMatrix * anchor.transform.localToWorldMatrix;
        box = Place(frame.position, forward, anchorToFrame, center, size, lengthMultiplier, widthMultiplier);
        return true;
    }

    /// <summary>월드 점이 상자 안인가(경계 포함) — EditMode 테스트·진단용.</summary>
    public bool Contains(Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(Rotation) * (point - Center);
        const float Eps = 1e-4f;
        return Mathf.Abs(local.x) <= HalfExtents.x + Eps &&
               Mathf.Abs(local.y) <= HalfExtents.y + Eps &&
               Mathf.Abs(local.z) <= HalfExtents.z + Eps;
    }

    // 정면 기준 프레임 = 앵커 조상 중 시전자 루트 바로 아래 노드(Armature). 몸 회전은 이 노드에 실리므로 이 노드 로컬로 바꿔 회전을 뺀다.
    private static Transform FrameOf(Transform anchor, Transform owner)
    {
        Transform t = anchor;
        while (t.parent != null && t.parent != owner)
            t = t.parent;
        return t.parent == owner ? t : owner;
    }
}
