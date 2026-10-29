using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공격 판정 도형 1개(할 일 153) — 프리팹 콜라이더 대신 코드로 계산하는 판정 형상. 공격 데이터 SO 가
/// <see cref="AttackShapeSet"/> 의 <c>[SerializeReference]</c> 목록으로 조합한다(특성 효과 <see cref="TraitEffect"/> 와 같은 방식 — 원소 숫자 칸이 xlsx 행).
/// 종류 4개: <see cref="AttackSectorShape"/>·<see cref="AttackCircleShape"/>·<see cref="AttackRectShape"/>·<see cref="AttackTriangleShape"/>.
///
/// 순수 C# 이다(Physics·MonoBehaviour 없음) — 판정 <see cref="Contains"/>, 넓은 질의 상자 <see cref="GetBroadBox"/>,
/// 배율 <see cref="Scaled"/>, 외곽선 <see cref="AppendOutlines"/> 가 <b>같은 로컬 수식</b>에서 나온다(기즈모 = 판정).
/// 판정은 바닥 평면 2D + 높이 범위(기준점 높이 기준 <see cref="minHeight"/>~<see cref="maxHeight"/>).
/// 설명: Docs/tech/attack-shapes.md.
/// </summary>
[Serializable]
public abstract class AttackShape
{
    /// <summary>경계 허용 오차(m). 외곽선 위의 점은 안이다.</summary>
    public const float Epsilon = 1e-4f;

    [Tooltip("기준점에서 정면으로 옮기는 거리(m, 음수 = 뒤)")]
    [SerializeField] protected float forwardOffset;

    [Tooltip("기준점에서 오른쪽으로 옮기는 거리(m, 음수 = 왼쪽)")]
    [SerializeField] protected float sideOffset;

    [Tooltip("도형을 기준 정면에서 돌리는 각도(도, + = 위에서 볼 때 시계 방향)")]
    [SerializeField] protected float yawOffset;

    [Tooltip("판정 높이 하한(m, 기준점 높이 기준)")]
    [SerializeField] protected float minHeight = -1f;

    [Tooltip("판정 높이 상한(m, 기준점 높이 기준)")]
    [SerializeField] protected float maxHeight = 3f;

    public float ForwardOffset { get => forwardOffset; set => forwardOffset = value; }
    public float SideOffset { get => sideOffset; set => sideOffset = value; }
    public float YawOffset { get => yawOffset; set => yawOffset = value; }
    public float MinHeight { get => minHeight; set => minHeight = value; }
    public float MaxHeight { get => maxHeight; set => maxHeight = value; }

    /// <summary>기준 포즈에 이 도형의 오프셋(앞·옆·회전)을 얹은 도형 자체 포즈.</summary>
    public AttackShapePose ShapePose(in AttackShapePose pose) => pose.Offset(forwardOffset, sideOffset, yawOffset);

    /// <summary>월드 점이 도형 안인가(경계 포함) — 높이 범위 + 바닥 평면 2D.</summary>
    public bool Contains(in AttackShapePose pose, Vector3 point)
    {
        float dy = point.y - pose.Position.y;
        if (dy < minHeight - Epsilon || dy > maxHeight + Epsilon)
            return false;

        return ContainsLocal(ShapePose(pose).ToPlanarLocal(point));
    }

    /// <summary>도형 로컬 바닥 평면(x 오른쪽, y 정면, 원점 = 도형 포즈)에서 안인가(경계 포함).</summary>
    public abstract bool ContainsLocal(Vector2 local);

    /// <summary>도형 로컬 바닥 평면 경계 사각형(x 오른쪽, y 정면).</summary>
    public abstract void GetLocalBounds(out Vector2 min, out Vector2 max);

    /// <summary>
    /// 넓은 질의(<c>Physics.OverlapBoxNonAlloc</c>)용 회전 상자 — 도형 로컬 경계 사각형 × 높이 범위. 이 상자 밖 점은 <see cref="Contains"/> 도 거짓이다.
    /// </summary>
    public void GetBroadBox(in AttackShapePose pose, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        GetLocalBounds(out Vector2 min, out Vector2 max);
        AttackShapePose shapePose = ShapePose(pose);
        float low = Mathf.Min(minHeight, maxHeight);
        float high = Mathf.Max(minHeight, maxHeight);
        center = shapePose.FromPlanarLocal((min + max) * 0.5f, (low + high) * 0.5f);
        halfExtents = new Vector3(
            Mathf.Max(0f, max.x - min.x) * 0.5f + Epsilon,
            (high - low) * 0.5f + Epsilon,
            Mathf.Max(0f, max.y - min.y) * 0.5f + Epsilon);
        rotation = Quaternion.LookRotation(shapePose.Forward, Vector3.up);
    }

    /// <summary>
    /// 특성 배율을 곱한 <b>복사본</b>(원본 SO 값은 그대로). <paramref name="lengthMultiplier"/> = 앞으로 뻗는 크기(부채꼴 range·원 radius·직사각형/삼각형 length),
    /// <paramref name="widthMultiplier"/> = 가로 크기(부채꼴 angle·직사각형/삼각형 width, 원은 없음). 음수는 0.
    /// </summary>
    public AttackShape Scaled(float lengthMultiplier, float widthMultiplier)
    {
        var copy = (AttackShape)MemberwiseClone();
        copy.ApplyScale(Mathf.Max(0f, lengthMultiplier), Mathf.Max(0f, widthMultiplier));
        return copy;
    }

    protected abstract void ApplyScale(float lengthMultiplier, float widthMultiplier);

    /// <summary>
    /// 외곽선 닫힌 고리들(월드, 높이 = 기준점 + <paramref name="height"/>)을 덧붙인다 — 판정 경계와 같은 수식. 도넛은 고리 2개.
    /// 에디터 기즈모(<c>AttackShapeDrawing</c>)·테스트가 쓴다.
    /// </summary>
    public void AppendOutlines(in AttackShapePose pose, List<Vector3[]> loops, float height = 0f, int arcSegments = 48)
    {
        var localLoops = new List<Vector2[]>();
        AppendLocalOutlines(localLoops, Mathf.Max(3, arcSegments));
        AttackShapePose shapePose = ShapePose(pose);
        foreach (Vector2[] local in localLoops)
        {
            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++)
                world[i] = shapePose.FromPlanarLocal(local[i], height); // 오프셋은 바닥 평면만 — 도형 포즈 높이 = 기준점 높이
            loops.Add(world);
        }
    }

    protected abstract void AppendLocalOutlines(List<Vector2[]> loops, int arcSegments);

    /// <summary>데이터 검증 — 음수 크기·NaN·높이 뒤집힘. 오류 문장을 덧붙인다.</summary>
    public virtual void Validate(List<string> errors)
    {
        Check(errors, nameof(forwardOffset), forwardOffset, allowNegative: true);
        Check(errors, nameof(sideOffset), sideOffset, allowNegative: true);
        Check(errors, nameof(yawOffset), yawOffset, allowNegative: true);
        Check(errors, nameof(minHeight), minHeight, allowNegative: true);
        Check(errors, nameof(maxHeight), maxHeight, allowNegative: true);
        if (minHeight > maxHeight)
            errors.Add($"{GetType().Name}: minHeight({minHeight}) > maxHeight({maxHeight})");
    }

    public override string ToString() => GetType().Name;

    // ── 공용 도우미 ─────────────────────────────────────────────────────

    protected void Check(List<string> errors, string field, float value, bool allowNegative = false)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            errors.Add($"{GetType().Name}.{field}: 숫자가 아니다({value})");
        else if (!allowNegative && value < 0f)
            errors.Add($"{GetType().Name}.{field}: 음수({value})");
    }

    /// <summary>원점 중심 반지름 <paramref name="radius"/> 의 호 점들(정면 = +y 에서 각 <paramref name="fromDeg"/>→<paramref name="toDeg"/>, + = 오른쪽).</summary>
    protected static void AddArc(List<Vector2> points, float radius, float fromDeg, float toDeg, int segments)
    {
        for (int i = 0; i <= segments; i++)
        {
            float rad = Mathf.Lerp(fromDeg, toDeg, i / (float)segments) * Mathf.Deg2Rad;
            points.Add(new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius));
        }
    }

    protected static Vector2[] Circle(float radius, int segments)
    {
        var points = new List<Vector2>(segments);
        AddArc(points, radius, 0f, 360f, segments);
        points.RemoveAt(points.Count - 1); // 끝점 = 시작점
        return points.ToArray();
    }
}
