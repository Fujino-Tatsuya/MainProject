using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공격 하나의 판정 영역(할 일 153) = 기준점 종류 + 도형 여러 개의 <b>합집합</b>(빼기 없음).
/// 공격 데이터 SO 에 필드로 둔다 — 도형 목록이 <c>[SerializeReference]</c> 라 원소 숫자 칸이 xlsx 행(<c>필드.shapes[0].range</c>)으로 나간다
/// (특성 효과와 같은 경로, Docs/tech/data-table.md §8). 인스펙터 '도형 추가' 는 <c>AttackShapeSetDrawer</c>.
/// 서버 판정은 <see cref="AttackShapeQuery"/>, 기즈모는 <c>AttackShapeDrawing</c>.
/// </summary>
[Serializable]
public sealed class AttackShapeSet
{
    [Tooltip("기준점 — 시전자 발밑+조준 방향 / 스킬이 정한 지점+방향")]
    [SerializeField] private AttackShapeOrigin origin = AttackShapeOrigin.CasterAim;

    [SerializeReference] private List<AttackShape> shapes = new List<AttackShape>();

    public AttackShapeSet() { }

    public AttackShapeSet(AttackShapeOrigin origin, params AttackShape[] shapes)
    {
        this.origin = origin;
        this.shapes = new List<AttackShape>(shapes ?? Array.Empty<AttackShape>());
    }

    public AttackShapeOrigin Origin => origin;
    public IReadOnlyList<AttackShape> Shapes => shapes;
    public bool IsEmpty => shapes == null || shapes.Count == 0;

    /// <summary>종류에 맞는 기준 포즈(<see cref="AttackShapePose.Resolve"/>).</summary>
    public AttackShapePose ResolvePose(Vector3 casterPosition, Vector3 aimDirection, Vector3 point, Vector3 pointDirection) =>
        AttackShapePose.Resolve(origin, casterPosition, aimDirection, point, pointDirection);

    /// <summary>도형 하나라도 점을 품는가(합집합). 빈 칸(null 원소)은 건너뛴다.</summary>
    public bool Contains(in AttackShapePose pose, Vector3 point) => AttackShapeQuery.AnyContains(shapes, pose, point);

    /// <summary>배율을 곱한 복사본(원본 그대로) — 특성 범위 배율(<see cref="AttackShape.Scaled"/>).</summary>
    public AttackShapeSet Scaled(float lengthMultiplier, float widthMultiplier)
    {
        var copy = new AttackShapeSet { origin = origin, shapes = new List<AttackShape>(shapes?.Count ?? 0) };
        if (shapes != null)
            foreach (AttackShape shape in shapes)
                copy.shapes.Add(shape?.Scaled(lengthMultiplier, widthMultiplier));
        return copy;
    }

    /// <summary>데이터 검증 — 빈 칸·도형별 오류.</summary>
    public void Validate(List<string> errors)
    {
        if (shapes == null)
            return;

        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] == null)
            {
                errors.Add($"shapes[{i}]: 비어 있다(도형 종류를 고를 것)");
                continue;
            }

            int before = errors.Count;
            shapes[i].Validate(errors);
            for (int e = before; e < errors.Count; e++)
                errors[e] = $"shapes[{i}] {errors[e]}";
        }
    }
}
