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

    LineRenderer _line;
    GameObject _customVisual;

    /// <summary>
    /// 패널 아래에 링을 만든다. 이미 있으면 그것을 돌려준다.
    ///
    /// <paramref name="customPrefab"/>을 주면 절차 생성 원 대신 그 프리팹을 쓴다 — 전용 아트(스프라이트·
    /// VFX·셰이더 원판 등)로 갈아끼우는 경로다. 이 경우 색·반지름·굵기는 프리팹이 스스로 정한다.
    /// </summary>
    public static ZoneInteractRing Create(Transform panel, float radius, Color color,
                                          float width, float groundLift, GameObject customPrefab)
    {
        if (panel == null) return null;

        ZoneInteractRing existing = panel.GetComponentInChildren<ZoneInteractRing>(true);
        if (existing != null) return existing;

        var go = new GameObject("InteractRing");
        go.transform.SetParent(panel, false);
        go.transform.localPosition = Vector3.up * groundLift;
        go.transform.localRotation = Quaternion.identity;

        var ring = go.AddComponent<ZoneInteractRing>();

        if (customPrefab != null) ring.BuildCustom(customPrefab);
        else ring.Build(radius, color, width);

        ring.SetVisible(false);
        return ring;
    }

    void BuildCustom(GameObject prefab)
    {
        _customVisual = Instantiate(prefab, transform);
        _customVisual.transform.localPosition = Vector3.zero;
        _customVisual.transform.localRotation = Quaternion.identity;
    }

    void Build(float radius, Color color, float width)
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = false;
        _line.loop = true;
        _line.positionCount = Segments;
        _line.widthMultiplier = Mathf.Max(0.01f, width);
        _line.numCornerVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.alignment = LineAlignment.TransformZ;   // 바닥에 눕힌다

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        _line.material = mat;
        _line.startColor = color;
        _line.endColor = color;

        // ⚠️ 여기서 평면을 두 번 눕히면 링이 세로로 선다(실제로 그렇게 났다).
        // 원은 로컬 **XY** 평면에 그리고, transform을 X축 +90°로 돌려 월드 XZ(바닥)로 눕힌다.
        // 그래야 `LineAlignment.TransformZ`의 기준축(로컬 Z)도 함께 수직이 되어 리본이 바닥에 깔린다.
        // (원을 로컬 XZ에 그린 뒤 또 90° 돌리면 월드 XY = 벽면이 된다.)
        for (int i = 0; i < Segments; i++)
        {
            float t = (float)i / Segments * Mathf.PI * 2f;
            _line.SetPosition(i, new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius, 0f));
        }

        transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    public void SetVisible(bool visible)
    {
        if (_line != null) _line.enabled = visible;
        if (_customVisual != null) _customVisual.SetActive(visible);
    }
}
