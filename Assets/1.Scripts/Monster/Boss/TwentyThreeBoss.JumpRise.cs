using UnityEngine;

/// <summary>
/// 점프 이륙 중 **모델만** 위로 끌어올려 카메라 밖으로 내보낸다(경석 10-21 — "카메라뷰에서 절대 안 보이게").
///
/// - 피어마다(각자 화면) 자기 카메라 절두체를 보고 "모델 경계가 시야에서 완전히 벗어나는 높이"를 구해 그만큼 올린다.
///   고정 높이를 쓰지 않는 이유: 카메라 거치·화면비가 바뀌어도 '안 보임'이 보장돼야 한다.
/// - 올리는 대상 = 애니메이터 오브젝트(메시 루트)의 로컬 위치. 보스 루트·피격 콜라이더·NetworkTransform 은 그대로다
///   (이륙 중 피격 판정은 이전대로 땅에 있다 — E19).
/// - 끝: 체공 시작에서 메시를 끄는 순간(SetModelVisibleClientRpc) 원위치. 숨긴 뒤라 되돌리는 것이 안 보인다.
/// </summary>
public partial class TwentyThreeBoss
{
    bool _riseActive;
    float _riseStart, _riseDuration, _riseHeight;
    Vector3 _riseBaseLocal;
    Transform _riseTarget;

    const float RiseMaxSearch = 300f;   // 높이 탐색 상한(m)
    const float RiseFallback = 60f;     // 카메라를 못 찾을 때
    const float RiseOvershoot = 1.25f;  // 끝나기 조금 전에 이미 화면 밖이 되도록(카메라 추적 흔들림 여유)

    /// <summary>[전 피어] 이륙 상승 시작 — 이륙 클립과 같은 길이.</summary>
    void BeginTakeoffRiseLocal(float duration)
    {
        StopTakeoffRiseLocal();
        if (animator == null || animator.transform == transform || duration <= 0.01f) return;   // 루트면 몸통이 같이 뜬다 — 안 한다

        _riseTarget = animator.transform;
        _riseBaseLocal = _riseTarget.localPosition;
        _riseStart = Time.time;
        _riseDuration = duration;
        _riseHeight = ComputeOffscreenRise() * RiseOvershoot;
        _riseActive = true;
    }

    /// <summary>[전 피어] 원위치(멱등).</summary>
    void StopTakeoffRiseLocal()
    {
        if (!_riseActive) return;
        _riseActive = false;
        if (_riseTarget != null) _riseTarget.localPosition = _riseBaseLocal;
    }

    // 애니메이션 평가 뒤에 덮어써야 한 프레임 밀리지 않는다.
    void LateUpdate()
    {
        if (!_riseActive || _riseTarget == null) return;
        float t = Mathf.Clamp01((Time.time - _riseStart) / _riseDuration);
        float worldUp = _riseHeight * t * t;   // 가속 상승 — 처음엔 클립 그대로, 끝으로 갈수록 확 솟는다
        Transform parent = _riseTarget.parent;
        float scaleY = parent != null ? Mathf.Max(0.0001f, parent.lossyScale.y) : 1f;
        _riseTarget.localPosition = _riseBaseLocal + Vector3.up * (worldUp / scaleY);
    }

    /// <summary>이 피어 카메라에서 모델 경계가 시야를 완전히 벗어나는 최소 상승 높이(m).</summary>
    float ComputeOffscreenRise()
    {
        Camera cam = Camera.main;
        if (cam == null) return RiseFallback;

        if (_modelRenderers == null) CacheModelRenderers();
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in _modelRenderers)
        {
            if (r == null || !r.enabled) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) return RiseFallback;

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        for (float h = 0f; h <= RiseMaxSearch; h += 1f)
        {
            var shifted = new Bounds(b.center + Vector3.up * h, b.size);
            if (!GeometryUtility.TestPlanesAABB(planes, shifted)) return h + 2f;
        }
        return RiseMaxSearch;
    }
}
