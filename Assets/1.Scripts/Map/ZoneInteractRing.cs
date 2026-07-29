using UnityEngine;

/// <summary>
/// 상호작용 완료 표시용 청록 링. 전 피어 로컬 연출이며 판정과는 무관하다.
///
/// 전용 아트가 없어 <see cref="LineRenderer"/>로 원을 만든다 — 존 프리팹에 새 에셋 의존을
/// 추가하지 않아도 되고, 색·반지름을 저작 데이터로 조절할 수 있다. URP에서 빌트인
/// <c>Default-Material</c>은 자홍색으로 깨지므로(이 프로젝트에서 이미 밟은 함정) <c>Sprites/Default</c>
/// 셰이더를 쓴다 — 라이팅을 받지 않아 어두운 맵에서도 색이 그대로 보인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZoneInteractRing : MonoBehaviour
{
    const int Segments = 48;
    const float LineWidth = 0.12f;
    const float GroundLift = 0.05f;   // Z-fighting 방지용 바닥 띄움

    LineRenderer _line;

    /// <summary>패널 아래에 링을 만든다. 이미 있으면 그것을 돌려준다.</summary>
    public static ZoneInteractRing Create(Transform panel, float radius, Color color)
    {
        if (panel == null) return null;

        ZoneInteractRing existing = panel.GetComponentInChildren<ZoneInteractRing>(true);
        if (existing != null) return existing;

        var go = new GameObject("InteractRing");
        go.transform.SetParent(panel, false);
        go.transform.localPosition = Vector3.up * GroundLift;
        go.transform.localRotation = Quaternion.identity;

        var ring = go.AddComponent<ZoneInteractRing>();
        ring.Build(radius, color);
        ring.SetVisible(false);
        return ring;
    }

    void Build(float radius, Color color)
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = false;
        _line.loop = true;
        _line.positionCount = Segments;
        _line.widthMultiplier = LineWidth;
        _line.numCornerVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.alignment = LineAlignment.TransformZ;   // 바닥에 눕힌다

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        _line.material = mat;
        _line.startColor = color;
        _line.endColor = color;

        // XZ 평면 원. 부모가 회전해도 로컬이라 함께 돈다.
        for (int i = 0; i < Segments; i++)
        {
            float t = (float)i / Segments * Mathf.PI * 2f;
            _line.SetPosition(i, new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius));
        }

        // LineRenderer는 로컬 Z를 법선으로 쓰므로 바닥에 눕히려면 X로 90도 돌린다.
        transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    public void SetVisible(bool visible)
    {
        if (_line != null) _line.enabled = visible;
    }
}
