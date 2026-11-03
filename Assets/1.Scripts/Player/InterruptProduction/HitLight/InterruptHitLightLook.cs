using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 적중 라이트 연출(V1·V2, 할 일 194) 수치 — 디렉터 칸 <c>hitLightLook</c> 이 가리키는 SO. V3(194 후속)는 같은 형식의 별도 에셋(<c>hitLightLookReference</c>)이라 V1·V2 값과 섞이지 않는다. 사건마다·프레임마다 읽는다(Play 중 바꾸면 다음 프레임부터).
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

    [Header("색 보정 — 채도·색 필터(모든 피어, V3 레퍼런스 맞춤)")]
    [Tooltip("색 보정 Volume 프로필 — 간파 전용 ColorAdjustments(채도 낮춤·보랏빛 색 필터, VP_InterruptHitLight_V3_Grade). 블룸처럼 이 피어 전용 전역 Volume 의 가중치만 움직인다. 비면 색 보정 없음(V1·V2)")]
    [SerializeField] private VolumeProfile gradeProfile;
    [Tooltip("색 보정 시간(초, 실시간) — 적중 순간 가중치 최대, 이 동안 곡선대로 0")]
    [SerializeField, Min(0f)] private float gradeDuration = 0.9f;
    [Tooltip("색 보정 가중치 곡선 — 가로 = 진행(0~1), 세로 = 가중치 [0, 1]")]
    [SerializeField] private AnimationCurve gradeCurve = Decay();

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

    [Header("V2 화이트아웃(본인 화면)")]
    [Tooltip("V2 적중 화이트아웃 길이(렌더 프레임 수) — 기존 ③ 화이트아웃 그림을 재사용(③ 칸과 무관하게 그린다). 0 = 없음")]
    [SerializeField, Min(0)] private int whiteoutFrames = 3;

    [Header("틈 수직 방향(발생 위치 = 틈 수직일 때만, 194 후속)")]
    [Tooltip("빛줄기 부채 퍼짐 반각(도) — 수직선에서 화면상 ± 이만큼 안에서 양쪽으로 뻗는다. 0 = 한 줄")]
    [SerializeField, Range(0f, 90f)] private float gapRaySpread = 20f;
    [Tooltip("빛줄기 줄 수(한 줄 = 양쪽으로 뻗는 날개 2개)")]
    [SerializeField, Range(1, 16)] private int gapRayCount = 5;
    [Tooltip("불꽃 퍼짐 반각(도) — 수직선 양쪽 방향 중심의 원뿔 안으로 튄다. 90 = 수직선 반구 전체")]
    [SerializeField, Range(0f, 90f)] private float gapSparkSpread = 30f;
    [Tooltip("가로 빛줄(V3) 방향 — 수직선(기본) / 시전자→대상 축. 둘 다 화면에서 그 각도로 보이게 카메라 쪽으로 기울인다")]
    [SerializeField] private InterruptHitLightStreakAxis gapStreakAxis = InterruptHitLightStreakAxis.Perpendicular;

    public Color LightColor => lightColor;
    public int WhiteoutFrames => whiteoutFrames;
    public float LightIntensity => lightIntensity;
    public float LightRange => lightRange;
    public LightShadows LightShadows => lightShadows;
    public float LightDuration => lightDuration;
    public AnimationCurve LightCurve => lightCurve;

    public VolumeProfile BloomProfile => bloomProfile;
    public float BloomDuration => bloomDuration;
    public AnimationCurve BloomCurve => bloomCurve;

    public VolumeProfile GradeProfile => gradeProfile;
    public float GradeDuration => gradeDuration;
    public AnimationCurve GradeCurve => gradeCurve;

    public float DarkAdaptSelf => darkAdaptSelf;
    public float DarkAdaptOthers => darkAdaptOthers;
    public float DarkAdaptDuration => darkAdaptDuration;
    public AnimationCurve DarkAdaptCurve => darkAdaptCurve;

    public float SunDimDepth => sunDimDepth;
    public float AmbientDimDepth => ambientDimDepth;
    public float SceneDimDuration => sceneDimDuration;
    public AnimationCurve SceneDimCurve => sceneDimCurve;

    public float GapRaySpread => gapRaySpread;
    public int GapRayCount => gapRayCount;
    public float GapSparkSpread => gapSparkSpread;
    public InterruptHitLightStreakAxis GapStreakAxis => gapStreakAxis;

    /// <summary>적중 순간 1 → 끝 0 으로 부드럽게 줄어드는 기본 곡선(Q5 — 약 0.9초에 걸쳐 복귀).</summary>
    public static AnimationCurve Decay() =>
        new AnimationCurve(new Keyframe(0f, 1f, 0f, -1.8f), new Keyframe(0.4f, 0.35f, -0.9f, -0.9f), new Keyframe(1f, 0f, -0.15f, 0f));
}

/// <summary>틈 수직 모드에서 가로 빛줄(V3 Streak)을 놓는 선(194 후속). 값은 직렬화되므로 순서를 바꾸지 않는다.</summary>
public enum InterruptHitLightStreakAxis : byte
{
    /// <summary>시전자→대상 축에 수직인 선(빛줄기와 같은 선).</summary>
    Perpendicular = 0,
    /// <summary>시전자→대상 축(레퍼런스 ZZZ 컷신은 무기 맞부딪힘 축을 따라 가로로 뻗는다).</summary>
    CasterToTarget = 1,
}
