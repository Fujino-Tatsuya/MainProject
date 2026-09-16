using UnityEngine;

/// <summary>
/// 커브대로 <b>자기 로컬 스케일을 반복해서 흔든다.</b>
///
/// 파티클의 <c>Size over Lifetime</c> 을 파티클 밖으로 꺼낸 것이다 — 데칼처럼 파티클 시스템이
/// 아닌 것에 같은 맥동을 주려고 만들었다. 저작 위젯이 같으므로(둘 다 <see cref="AnimationCurve"/>)
/// 파티클에 그려 둔 커브를 그대로 옮겨 그리면 된다.
///
/// <b>왜 Animator + 컨트롤러가 아닌가.</b> 애니메이션 클립으로도 되지만
/// (URP <c>DecalProjector</c> 는 <c>OnDidApplyAnimationProperties</c> 로 직렬화 필드 애니를 받아 준다),
/// 루프 상태 하나를 위해 클립·컨트롤러 자산 둘과 Animator 평가 비용이 붙는다.
/// 장판은 동시에 여러 개 깔리므로 그 비용이 실제로 는다.
///
/// <b>부모와 싸우지 않는다.</b> 이 컴포넌트는 <b>자기</b> 로컬 스케일만 만진다 —
/// 크기(반경)는 부모가 정하고 여기서는 그 위에 배율만 얹는 구조라, 예컨대
/// <c>AreaZone</c> 이 부모를 반경대로 스케일해도 서로 덮어쓰지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class ScalePulse : MonoBehaviour
{
    [Tooltip("시간(초) → 스케일 배율. 1 이 원래 크기다.\n" +
             "커브 끝을 넘어가면 postWrapMode 대로 반복한다 — 커브 창 우상단에서 Loop/PingPong 을 고를 것")]
    [SerializeField] AnimationCurve pulse = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.5f, 1.15f), new Keyframe(1f, 1f));

    [Tooltip("커브 한 바퀴에 걸리는 시간(초). 커브의 가로축을 이 값으로 늘여 쓴다 — " +
             "커브는 0~1 로 그려 두고 여기서 속도를 조절하면 된다")]
    [SerializeField, Min(0.01f)] float period = 1.2f;

    [Tooltip("Z 축에도 배율을 적용한다.\n" +
             "🔴 데칼에 붙일 때는 꺼 둘 것 — 데칼의 Z 는 크기가 아니라 **투영 깊이**라 " +
             "함께 흔들면 경사면에서 칠해지는 범위가 출렁인다")]
    [SerializeField] bool includeZ = false;

    [Tooltip("시작 위상(0~1). 같은 연출을 여러 개 깔 때 이 값을 흩어 두면 동시에 뛰지 않는다")]
    [SerializeField, Range(0f, 1f)] float phaseOffset;

    Vector3 _baseScale = Vector3.one;
    float _elapsed;

    void Awake()
    {
        _baseScale = transform.localScale;

        // 커브를 그린 사람이 랩 모드를 안 건드렸어도 반복하게 해 둔다 —
        // 기본값(ClampForever)이면 한 바퀴 돌고 멈춰서 "맥동이 한 번만 뛴다"가 된다.
        if (pulse != null && pulse.postWrapMode == WrapMode.ClampForever)
            pulse.postWrapMode = WrapMode.Loop;
    }

    void OnEnable() => _elapsed = phaseOffset * period;

    void Update()
    {
        if (pulse == null || pulse.length == 0) return;

        _elapsed += Time.deltaTime;

        float k = pulse.Evaluate(_elapsed / period);

        transform.localScale = new Vector3(
            _baseScale.x * k,
            _baseScale.y * k,
            includeZ ? _baseScale.z * k : _baseScale.z);
    }
}
