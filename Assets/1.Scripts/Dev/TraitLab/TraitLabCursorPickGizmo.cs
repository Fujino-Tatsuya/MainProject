#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 특성 랩 빗나감 커서 범위 기즈모(할 일 265 후속) — <see cref="DebugTraitLab"/> 의 OnDrawGizmos 가 부른다. 에디터 전용.
/// 판정을 다시 하지 않는다 — 스킬 조준·호버 외곽선이 이번 프레임 실제로 부른 <see cref="CursorUnitPick"/> 결과(<see cref="CursorUnitPick.LastTrace"/>)를 그린다.
/// 커서 바닥 지점에 반경 원(<see cref="PlayerInputReader.CursorPickRadius"/>) + 잡힌 유닛까지 선. 색 = 직접 맞음(빨강) / 반경으로 잡음(노랑) / 못 잡음(회색).
/// Gizmos 라 Game 뷰는 상단 Gizmos 버튼이 켜져 있어야 보인다(공격 범위 기즈모와 같다).
/// </summary>
public sealed class TraitLabCursorPickGizmo
{
    public static readonly Color DirectColor = new Color(1f, 0.15f, 0.15f);
    public static readonly Color LenientColor = new Color(1f, 0.9f, 0.1f);
    public static readonly Color MissColor = new Color(0.7f, 0.7f, 0.7f);
    const int CircleSegments = 48;
    const float Lift = 0.05f;   // 바닥에 파묻히지 않게
    const int StaleFrames = 2;  // 이보다 오래된 결과 = 이번 프레임 판정 없음

    public bool Enabled = true;
    GUIStyle _labelStyle;

    public void Draw()
    {
        if (!Enabled || !Application.isPlaying)
            return;

        CursorUnitPick.Trace trace = CursorUnitPick.LastTrace;
        if (!trace.HasGroundPoint || Time.frameCount - trace.Frame > StaleFrames)
            return;

        bool picked = trace.Result == CursorPickResult.ValidTarget && trace.Unit != null;
        Color color = !picked ? MissColor : trace.DirectHit ? DirectColor : LenientColor;
        string text = !picked ? "못 잡음" : trace.DirectHit ? "직접 맞음" : "반경으로 잡음";

        Color previous = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = color;

        Vector3 center = trace.GroundPoint + Vector3.up * Lift;
        if (trace.Radius > 0f)
            DrawCircle(center, trace.Radius);
        Gizmos.DrawLine(center + Vector3.left * 0.15f, center + Vector3.right * 0.15f);
        Gizmos.DrawLine(center + Vector3.back * 0.15f, center + Vector3.forward * 0.15f);

        if (picked)
        {
            Vector3 unitAt = trace.Unit.transform.position + Vector3.up * Lift;
            Gizmos.DrawLine(center, unitAt);
            Gizmos.DrawWireSphere(unitAt, 0.2f);
        }

        Gizmos.color = previous;
        Gizmos.matrix = previousMatrix;

        _labelStyle ??= new GUIStyle(EditorStyles.boldLabel);
        _labelStyle.normal.textColor = color;
        Handles.Label(center + Vector3.up * 0.3f, $"커서 {trace.Radius:0.##}m · {text}", _labelStyle);
    }

    static void DrawCircle(Vector3 center, float radius)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= CircleSegments; i++)
        {
            float a = i * (Mathf.PI * 2f / CircleSegments);
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
#endif
