using UnityEngine;

// 보스 입장 범위 표시 (PLAN §6 개정 — 로아식). 모든 피어 로컬 연출.
// 트리거 범위(패드)를 바닥 테두리 라인으로 그리고, 진입/카운트다운 상태(복제값)에 따라 색을 바꾼다.
// 색상 값은 BossTeleportManager 인스펙터에서 튜닝(이 컴포넌트는 런타임 부착이라 씬에서 못 만짐).
// MapContentSpawner가 BossRoom 존 스폰 시 부착한다.
public class BossEnterZoneVisual : MonoBehaviour
{
    // 매니저 부재 시 폴백 색(정상 흐름에서는 항상 매니저 값 사용).
    private static readonly Color FallbackIdle = new Color(0.25f, 0.8f, 1f, 0.9f);
    private static readonly Color FallbackActive = new Color(0.35f, 1f, 0.4f, 1f);

    private LineRenderer _line;
    private Material _material;
    private Color _current;

    /// <summary>존 로컬 기준 테두리 생성. center/size는 트리거 박스와 동일 값 사용.</summary>
    public void Setup(Vector3 centerLocal, Vector2 sizeXZ)
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = false;
        _line.loop = true;
        _line.positionCount = 4;
        _line.startWidth = _line.endWidth = 0.2f;

        // 바닥 살짝 위에 사각 테두리 (존 로컬 — 존 회전을 따라간다)
        float y = 0.15f;
        float hx = sizeXZ.x * 0.5f, hz = sizeXZ.y * 0.5f;
        _line.SetPositions(new[]
        {
            new Vector3(centerLocal.x - hx, y, centerLocal.z - hz),
            new Vector3(centerLocal.x - hx, y, centerLocal.z + hz),
            new Vector3(centerLocal.x + hx, y, centerLocal.z + hz),
            new Vector3(centerLocal.x + hx, y, centerLocal.z - hz),
        });

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        _material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        _line.material = _material;

        BossTeleportManager manager = BossTeleportManager.Instance;
        ApplyColor(manager != null ? manager.IdleColor : FallbackIdle);
    }

    private void Update()
    {
        BossTeleportManager manager = BossTeleportManager.Instance;
        bool active = manager != null && (manager.IsOccupied || manager.IsCountdownActive);
        Color target = manager != null
            ? (active ? manager.ActiveColor : manager.IdleColor)
            : (active ? FallbackActive : FallbackIdle);
        if (target != _current)
            ApplyColor(target);
    }

    private void ApplyColor(Color color)
    {
        _current = color;
        if (_line == null) return;
        _line.startColor = _line.endColor = color;
        if (_material != null) _material.color = color;
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
