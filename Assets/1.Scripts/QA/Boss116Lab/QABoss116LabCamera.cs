#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩 카메라(씬 배치형, 이 피어 화면만). 플레이어 vcam 이 플레이어와 23호의 가운데 점을 따라가고,
/// 둘 사이가 멀어지면 팔로우 거리를 늘린다 — 사람이 화면으로 대시·막힘·창을 같이 본다.
/// 카메라 코드는 그대로다: 기존 공개 API <see cref="CameraTargetSwitcher.SetTemporaryFollow"/>·<see cref="CameraTargetSwitcher.SetCinematicDistanceMultiplier"/> 만 부른다
/// (벽 가림·포그 기준 대상은 그대로 플레이어). 꺼지면 원래대로 되돌린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class QABoss116LabCamera : MonoBehaviour
{
    [Tooltip("둘 사이 이 거리(m)까지는 기본 팔로우 거리.")]
    [SerializeField, Min(0f)] private float nearMeters = 5f;
    [Tooltip("m 당 팔로우 거리 배율 증가.")]
    [SerializeField, Min(0f)] private float perMeter = 0.07f;
    [Tooltip("팔로우 거리 배율 상한.")]
    [SerializeField, Min(1f)] private float maxMultiplier = 1.9f;
    [Tooltip("가운데 점 이동 감쇠(초) — 0 = 바로.")]
    [SerializeField, Min(0f)] private float smoothSeconds = 0.15f;

    private Transform _proxy;
    private CameraTargetSwitcher _bound;
    private TwentyThreeBoss _boss;
    private float _nextFind;

    private void Update()
    {
        CameraTargetSwitcher cam = CameraTargetSwitcher.Active;
        Player me = Player.LocalPlayer;
        if ((_boss == null || !_boss.isActiveAndEnabled) && Time.unscaledTime >= _nextFind)
        {
            _nextFind = Time.unscaledTime + 1f;
            _boss = FindAnyObjectByType<TwentyThreeBoss>();
        }
        if (cam == null || me == null || _boss == null)
        {
            Release();
            return;
        }
        if (_proxy == null)
        {
            _proxy = new GameObject("[QA116Lab] CameraMid").transform;
            _proxy.SetParent(transform, false);
            _proxy.position = me.transform.position;
        }
        Vector3 a = me.transform.position, b = _boss.transform.position;
        Vector3 mid = (a + b) * 0.5f;
        mid.y = a.y;
        float k = smoothSeconds > 0f ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothSeconds) : 1f;
        _proxy.position = Vector3.Lerp(_proxy.position, mid, k);
        float dist = new Vector2(a.x - b.x, a.z - b.z).magnitude;
        if (_bound != cam || !cam.HasTemporaryFollow)
        {
            cam.SetTemporaryFollow(_proxy);
            _bound = cam;
        }
        cam.SetCinematicDistanceMultiplier(Mathf.Clamp(1f + Mathf.Max(0f, dist - nearMeters) * perMeter, 1f, maxMultiplier));
    }

    private void OnDisable() => Release();

    private void Release()
    {
        if (_bound == null)
            return;
        _bound.ClearTemporaryFollow();
        _bound.SetCinematicDistanceMultiplier(1f);
        _bound = null;
    }
}
#endif
