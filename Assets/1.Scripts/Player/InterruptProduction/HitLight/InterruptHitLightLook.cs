using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 적중 라이트 연출(V1·V2, 할 일 194) 수치 — 디렉터 칸 <c>hitLightLook</c> 이 가리키는 SO. 사건마다·프레임마다 읽는다(Play 중 바꾸면 다음 프레임부터).
/// 시간은 전부 실시간(unscaled, Q8) — 슬로우·히트스톱과 무관. 데이터 테이블에는 내지 않는다(PLAN 194 §3 — 범위 밖).
/// 문서: Docs/tech/interrupt-presentation.md §9.
/// </summary>
[CreateAssetMenu(menuName = "BeaverLobby/Interrupt/Hit Light Look", fileName = "InterruptHitLightLook")]
public sealed class InterruptHitLightLook : ScriptableObject
{
    [Header("적중 포인트 라이트(모든 피어)")]
    [Tooltip("라이트 색 — 따뜻한 흰주황(Q13)")]
    [SerializeField] private Color lightColor = new Color(1f, 0.75f, 0.45f, 1f);
    [Tooltip("라이트 최대 세기(곡선 1 일 때). 0 = 라이트 없음")]
    [SerializeField, Min(0f)] private float lightIntensity = 20f;
    [Tooltip("라이트 반경(m, 연출 루트 스케일을 곱한다)")]
    [SerializeField, Min(0.01f)] private float lightRange = 8f;
    [Tooltip("라이트 그림자 — 기본 끔(추가 라이트 그림자 비용)")]
    [SerializeField] private LightShadows lightShadows = LightShadows.None;
    [Tooltip("라이트가 켜져 있는 시간(초, 실시간) — 이 동안 곡선대로 꺼진다")]
    [SerializeField, Min(0f)] private float lightDuration = 0.9f;
    [Tooltip("라이트 세기 곡선 — 가로 = 진행(0~1), 세로 = 최대 세기 대비 배율. 끝에서 0 이 되게")]
    [SerializeField] private AnimationCurve lightCurve = Decay();

    [Header("블룸(모든 피어)")]
    [Tooltip("블룸 Volume 프로필 — 간파 전용 블룸(VP_InterruptHitFlash_Bloom). 이 피어 전용 전역 Volume 의 가중치만 움직인다(에셋 값은 그대로). 비면 블룸 없음")]
    [SerializeField] private VolumeProfile bloomProfile;
    [Tooltip("블룸 시간(초, 실시간) — 적중 순간 가중치 최대, 이 동안 곡선대로 0")]
    [SerializeField, Min(0f)] private float bloomDuration = 0.9f;
    [Tooltip("블룸 가중치 곡선 — 가로 = 진행(0~1), 세로 = 가중치 [0, 1]")]
    [SerializeField] private AnimationCurve bloomCurve = Decay();

    [Header("암순응 가 — 화면 패스(어두운 픽셀만, 라이트·이펙트는 남음)")]
    [Tooltip("발동자 본인 화면 최대 어둡힘 [0, 1](Q9 — 오너 1)")]
    [SerializeField, Range(0f, 1f)] private float darkAdaptSelf = 1f;
    [Tooltip("팀원 화면 최대 어둡힘 [0, 1](Q9 — 타인 0.35)")]
    [SerializeField, Range(0f, 1f)] private float darkAdaptOthers = 0.35f;
    [Tooltip("적중 순간 어두워졌다가 원래로 돌아오는 시간(초, 실시간, Q5)")]
    [SerializeField, Min(0f)] private float darkAdaptDuration = 0.9f;
    [Tooltip("어둡힘 곡선 — 가로 = 진행(0~1), 세로 = 최대 어둡힘 대비 배율. 끝에서 0 이 되게")]
    [SerializeField] private AnimationCurve darkAdaptCurve = Decay();

    [Header("암순응 나 — 태양광·환경광 낮춤(모든 화면 같은 세기)")]
    [Tooltip("태양광(Directional Light)을 적중 순간 이 비율만큼 낮춘다 [0, 1] — 1 = 완전히 끔")]
    [SerializeField, Range(0f, 1f)] private float sunDimDepth = 0.85f;
    [Tooltip("환경광을 적중 순간 이 비율만큼 낮춘다 [0, 1]")]
    [SerializeField, Range(0f, 1f)] private float ambientDimDepth = 0.7f;
    [Tooltip("낮췄다가 원래로 돌아오는 시간(초, 실시간)")]
    [SerializeField, Min(0f)] private float sceneDimDuration = 0.9f;
    [Tooltip("낮춤 곡선 — 가로 = 진행(0~1), 세로 = 낮춤 비율 대비 배율. 끝에서 0 이 되게")]
    [SerializeField] private AnimationCurve sceneDimCurve = Decay();

    public Color LightColor => lightColor;
    public float LightIntensity => lightIntensity;
    public float LightRange => lightRange;
    public LightShadows LightShadows => lightShadows;
    public float LightDuration => lightDuration;
    public AnimationCurve LightCurve => lightCurve;

    public VolumeProfile BloomProfile => bloomProfile;
    public float BloomDuration => bloomDuration;
    public AnimationCurve BloomCurve => bloomCurve;

    public float DarkAdaptSelf => darkAdaptSelf;
    public float DarkAdaptOthers => darkAdaptOthers;
    public float DarkAdaptDuration => darkAdaptDuration;
    public AnimationCurve DarkAdaptCurve => darkAdaptCurve;

    public float SunDimDepth => sunDimDepth;
    public float AmbientDimDepth => ambientDimDepth;
    public float SceneDimDuration => sceneDimDuration;
    public AnimationCurve SceneDimCurve => sceneDimCurve;

    /// <summary>적중 순간 1 → 끝 0 으로 부드럽게 줄어드는 기본 곡선(Q5 — 약 0.9초에 걸쳐 복귀).</summary>
    public static AnimationCurve Decay() =>
        new AnimationCurve(new Keyframe(0f, 1f, 0f, -1.8f), new Keyframe(0.4f, 0.35f, -0.9f, -0.9f), new Keyframe(1f, 0f, -0.15f, 0f));
}
