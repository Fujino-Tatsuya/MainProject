using UnityEngine;

// 물 머티리얼 UV 스크롤 — 오염수가 흐르는 느낌.
// Renderer의 머티리얼 '인스턴스' 오프셋을 시간에 따라 이동(공유 머티리얼 오염 방지).
// 비네트워크 시각 효과 — Time.time 기반이라 클라마다 자연스럽게 흐름(동기화 불필요).
[RequireComponent(typeof(Renderer))]
public class WaterScroll : MonoBehaviour
{
    [Tooltip("초당 UV 이동 속도 (x, y).")]
    public Vector2 Speed = new Vector2(0.04f, 0.015f);
    [Tooltip("스크롤할 텍스처 프로퍼티. URP Lit BaseMap = _BaseMap.")]
    public string TextureProperty = "_BaseMap";

    private Material _mat;
    private int _propId;
    private Vector2 _offset;

    private void Awake()
    {
        _mat = GetComponent<Renderer>().material; // 인스턴스화
        _propId = Shader.PropertyToID(TextureProperty);
    }

    private void Update()
    {
        if (_mat == null || !_mat.HasProperty(_propId)) return;
        // 누적 + 0~1 래핑 → Time.time 무한증가 정밀도 드리프트 방지
        _offset += Speed * Time.deltaTime;
        _offset.x = Mathf.Repeat(_offset.x, 1f);
        _offset.y = Mathf.Repeat(_offset.y, 1f);
        _mat.SetTextureOffset(_propId, _offset);
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat); // 인스턴스 머티리얼 누수 방지
    }
}
