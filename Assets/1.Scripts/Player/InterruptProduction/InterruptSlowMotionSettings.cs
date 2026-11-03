using UnityEngine;

/// <summary>
/// 간파 슬로우 3단계 수치(할 일 194 후속 2) — ① 대쉬 간파 시도 · ② 간파 준비(근접 간파 시도) · ③ 간파 적중(성공).
/// 옛 <see cref="InterruptProductionDirector"/>(NetworkManager.prefab) 칸과 <see cref="CameraTargetSwitcher"/>(CameraSwitcher.prefab) 성공 줌 칸을
/// 필드 이름 그대로 옮겼다 — 데이터 테이블 대상 열만 이 에셋 이름(InterruptSlowMotionSettings)으로 바뀐다(병합 Export 필요, data-table.md §2).
/// <list type="bullet">
/// <item>디렉터·카메라는 사건마다·프레임마다 이 SO 를 읽는다(캐싱 없음) — Play 중 인스펙터에서 바꾸면 다음 사건부터.</item>
/// <item>슬로우 프로필은 서버 값만 쓰인다(서버가 Start 이벤트에 7칸을 실어 보낸다). 줌 배율은 각 피어 값(A3 오너 줌 칸이 켜져야 쓰인다).</item>
/// <item>스위치(A1 히트스톱·A3 오너 줌)는 디렉터에 남는다 — 이 SO 는 수치만.</item>
/// </list>
/// 문서: Docs/tech/interrupt-presentation.md §9.8.
/// </summary>
[CreateAssetMenu(menuName = "BeaverLobby/Interrupt/Slow Motion Settings", fileName = "InterruptSlowMotionSettings")]
[DataTableSheet("SlowMotion", Order = 0)]
public sealed class InterruptSlowMotionSettings : ScriptableObject
{
    /// <summary>성공 줌 기본값 — SO 가 비었을 때 카메라가 쓴다.</summary>
    public const float DefaultSlowMotionZoomFactor = 0.85f;

    // ① 대쉬 간파 시도 — 대시에 실린 간파를 서버가 승인한 순간 건다(대시 끝을 기다리지 않는다). 강·약은 ② 와 같은 예측(대시 끝 지점 기준)으로 고른다.
    // 처음 값 = ② 강·약 복사(동작 변화 없음). 대시 남은 시간이 유지 구간을 먹으므로 hold 를 따로 튜닝할 수 있게 나눴다. 줌은 ② 줌을 같이 쓴다.
    [Header("① 대쉬 간파 시도")]
    [Tooltip("대쉬 간파 시도 슬로우 모양 — 성공 예측(강). 서버 값만 쓴다. 처음 값 = 간파 준비 강(interruptProfile) 복사. 줌은 interruptZoomFactor 공유")]
    [SerializeField] private SlowMotionProfile interruptDashProfile = StrongAttemptDefaults();
    [Tooltip("대쉬 간파 시도 슬로우 모양 — 실패 예측(약). 서버 값만 쓴다. 처음 값 = 간파 준비 약(interruptWeakProfile) 복사. 줌은 interruptWeakZoomFactor 공유")]
    [SerializeField] private SlowMotionProfile interruptDashWeakProfile = WeakAttemptDefaults();

    // ② 간파 준비 — 근접 간파 시도(우클릭) 승인 순간. 대상 없음 예측이면 슬로우 없음.
    [Header("② 간파 준비 (근접 간파 시도)")]
    [Tooltip("간파 시도 슬로우 모양 — 성공 예측(강). 서버 값만 쓴다")]
    [SerializeField] private SlowMotionProfile interruptProfile = StrongAttemptDefaults();
    [Tooltip("간파 시도 슬로우 모양 — 실패 예측(약). 서버 값만 쓴다(할 일 132)")]
    [SerializeField] private SlowMotionProfile interruptWeakProfile = WeakAttemptDefaults();
    [Tooltip("내가 일으킨 강 시도 슬로우(근접·대쉬 공통)의 유지 단계 카메라 거리 배율. [0.1, 1] — 1 이면 줌 없음. 이 피어 값을 쓴다(A3 칸이 켜져야 쓰인다)")]
    [SerializeField, Range(SlowMotionCameraZoom.MinZoomFactor, 1f)] private float interruptZoomFactor = 0.7f;
    [Tooltip("내가 일으킨 약 시도 슬로우(근접·대쉬 공통)의 유지 단계 카메라 거리 배율. [0.1, 1] — 1 이면 줌 없음. 이 피어 값을 쓴다(A3 칸이 켜져야 쓰인다)")]
    [SerializeField, Range(SlowMotionCameraZoom.MinZoomFactor, 1f)] private float interruptWeakZoomFactor = 0.93f;

    // ③ 간파 적중 — 카운터 창을 소비한 성공(근접·대쉬·제압 공통). 시도 슬로우의 지금 배율에서 이어진다.
    [Header("③ 간파 적중 (성공)")]
    [Tooltip("간파 성공(카운터 창 소비) 시 다시 거는 슬로우 모양. 맨 앞 히트스톱(hitStopDuration·hitStopScale) 뒤 그 배율에서 진입한다(할 일 87). " +
             "서버 값만 쓴다. 히트스톱 켜기/끄기 = 디렉터 A1 칸")]
    [SerializeField] private SlowMotionProfile interruptSuccessProfile = new SlowMotionProfile
    {
        scale = 0.25f,
        hitStopDuration = 0.05f,
        hitStopScale = 0.02f,
        enterDuration = 0.05f,
        holdDuration = 1.2f,
        exitDuration = 0.4f,
        failExitDuration = 0.1f,
    };
    [Tooltip("내가 일으킨 간파 성공 슬로우의 유지 단계에서 플레이어 카메라 거리 배율. [0.1, 1] — 1 이면 줌 없음 (PLAN-interrupt-slowmo D10). " +
             "시도에서 이어진 성공은 둘 중 더 깊은 쪽(강 0.7 → 성공은 0.7 유지). 이 피어 값을 쓴다(A3 칸이 켜져야 쓰인다)")]
    [SerializeField, Range(SlowMotionCameraZoom.MinZoomFactor, 1f)] private float slowMotionZoomFactor = DefaultSlowMotionZoomFactor;

    // ④ 카메라 대상 중심(할 일 196) — 간파 클릭 즉시 오너 화면 카메라 중심을 간파 대상 쪽으로(강·약 예측, 대상 없음 = 그대로). 줌(위 배율)은 그대로, 중심만.
    // 켜기/끄기 = 디렉터 칸(cameraCenter). 이 피어 값을 쓴다(오너 화면만). 시간은 실제 시간(슬로우·히트스톱 무관).
    [Header("④ 카메라 대상 중심 (할 일 196)")]
    [Tooltip("간파 클릭 순간 카메라 중심이 대상 쪽으로 옮겨 가는 시간(초, 실제 시간). 0 = 순간 이동")]
    [SerializeField, Min(0f)] private float cameraCenterMoveDuration = 0.08f;
    [Tooltip("대상 쪽 이동 비율 [0, 1] — 1 = 대상이 화면 정중앙, 0.5 = 평소 중심과 대상 사이 절반, 0 = 옮기지 않음")]
    [SerializeField, Range(0f, 1f)] private float cameraCenterRatio = 1f;
    [Tooltip("평소 중심으로 돌아가는 시간(초, 실제 시간). 판정 실패·서버 거부 = 그 순간, 성공 = 적중 슬로우 복귀 구간 시작에 맞춰 시작. 0 = 순간 복귀")]
    [SerializeField, Min(0f)] private float cameraCenterReturnDuration = 0.25f;

    public SlowMotionProfile InterruptDashProfile => interruptDashProfile;
    public SlowMotionProfile InterruptDashWeakProfile => interruptDashWeakProfile;
    public SlowMotionProfile InterruptProfile => interruptProfile;
    public SlowMotionProfile InterruptWeakProfile => interruptWeakProfile;
    public float InterruptZoomFactor => interruptZoomFactor;
    public float InterruptWeakZoomFactor => interruptWeakZoomFactor;
    public SlowMotionProfile InterruptSuccessProfile => interruptSuccessProfile;
    public float SlowMotionZoomFactor => slowMotionZoomFactor;
    public float CameraCenterMoveDuration => cameraCenterMoveDuration;
    public float CameraCenterRatio => cameraCenterRatio;
    public float CameraCenterReturnDuration => cameraCenterReturnDuration;

    private static SlowMotionProfile StrongAttemptDefaults() => new SlowMotionProfile
    {
        scale = 0.15f,
        hitStopDuration = 0f,
        hitStopScale = 0.02f,
        enterDuration = 0.05f,
        holdDuration = 0.5f,
        exitDuration = 0.25f,
        failExitDuration = 0.1f,
    };

    private static SlowMotionProfile WeakAttemptDefaults() => new SlowMotionProfile
    {
        scale = 0.5f,
        hitStopDuration = 0f,
        hitStopScale = 0.02f,
        enterDuration = 0.05f,
        holdDuration = 0.2f,
        exitDuration = 0.25f,
        failExitDuration = 0.1f,
    };

    private static InterruptSlowMotionSettings _fallback;

    /// <summary>
    /// 참조가 비었을 때(EditMode 테스트·새 컴포넌트) 쓰는 기본값 인스턴스 — 코드 처음 값(= 이관 당시 프리팹 값). 저장하지 않는다.
    /// </summary>
    public static InterruptSlowMotionSettings OrDefault(InterruptSlowMotionSettings settings)
    {
        if (settings != null)
            return settings;

        if (_fallback == null)
        {
            _fallback = CreateInstance<InterruptSlowMotionSettings>();
            _fallback.name = "InterruptSlowMotionSettings (기본값)";
            _fallback.hideFlags = HideFlags.HideAndDontSave;
        }

        return _fallback;
    }
}
