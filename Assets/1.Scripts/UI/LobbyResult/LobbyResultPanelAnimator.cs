using System;
using System.Collections.Generic;
using EuniTween;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215) — 표시 전용. <see cref="LobbyResultPanel"/> 의 <c>Content</c> 에 붙는다.
/// (a) 창 등장: 열 때 크기(<c>hiddenScale</c>→1)·투명도(0→1), 닫을 때 반대(짧게).
/// (c) 숫자 카운트업: 열 때 플레이어 행 숫자(<see cref="ResultStatsView.SetCountUpProgress"/>)와 소요 시간
///     (<see cref="LobbyResultPanel.SetPlayTimeCountUp"/>)이 같은 진행도로 0 에서 최종값까지 올라간다.
///     기본 곡선은 강한 감속 — 82% 까지 빠르게, 남은 자리는 시간의 70% 동안 천천히(할 일 220).
///     값이 작을수록 선형 쪽으로 섞어(<c>linearMaxSteps</c>~<c>curveFullSteps</c>) 정수가 고르게 오르고, 모든 값이 같은 순간 최종값.
/// (c2) 카운트업이 끝나는 순간(최종값 도달) 창 본체가 살짝 커졌다 돌아오는 펀치(EuniTween <c>Easing.Punch</c>, 할 일 220)
/// (c3) 등급 도장(할 일 223): 펀치가 끝나면 도장(<see cref="LobbyResultPanel.GradeStamp"/>)이 크게·투명·기운 채로 0.12초 만에 내려찍히고(InQuad),
///      착지 순간 창 본체가 짧게 흔들리고 도장이 작게 튀었다 자리 잡는다. 사운드 키(비우면 무음).
/// (d) 1위 왕관: 도장이 자리 잡은 뒤(도장 없으면 펀치 끝)부터 <c>crownRepeatInterval</c>(3초)마다 점프하며 한 바퀴(기본 Y축) 돌고,
///     착지마다 반짝임이 크게 터진 뒤 다음 점프 전까지 은은하게 반복한다(EuniTween 이징). 행 밖으로 튀어나가도 자르지 않는다.
///
/// - 시간 = <c>Time.unscaledDeltaTime</c>(슬로모션 영향 없음). 곡선·지속시간 = 이 컴포넌트 인스펙터.
/// - 열고 닫기 판단·<c>Closed</c> 이벤트·ESC 점유는 패널이 그대로 한다. 이 컴포넌트는 닫힘 연출이 끝난 뒤
///   패널이 넘긴 콜백(= <c>Content</c> 끄기)만 늦게 부른다. 이 컴포넌트가 없으면 패널은 예전처럼 즉시 켜고 끈다.
/// - 닫힘 연출이 시작되면 바로 입력을 돌려준다: <see cref="UiModalBlocker"/> 를 끄고 CanvasGroup 의 클릭 막기를 푼다.
/// - CanvasGroup 은 프리팹에 따로 두지 않는다 — 없으면 런타임에 붙인다.
/// - 반짝임 = 왕관 자식에 Image 몇 개(<c>sparkleSprite</c> 한 칸 — 교체는 그 칸만). 파티클이 아니라 Overlay 캔버스에서 그대로 보인다.
///   런타임 텍스처·메시 생성 없음(Image 오브젝트만 만들고, 행이 다시 그려지면 왕관과 함께 사라진다).
/// - 연결 = 메뉴 <c>Tools/UI/로비 결과창 애니 연결</c>(병합 충돌 때 다시 돌리면 같은 결과). 사용법 = Docs/tech/lobby-result-panel.md.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyResultPanelAnimator : MonoBehaviour
{
    [Tooltip("크기를 바꿀 대상(창 본체 'Panel'). 비어 있으면 자식 'Panel' 을 찾는다. Dim 은 크기 그대로, 투명도만 같이 바뀐다.")]
    [SerializeField] private RectTransform scaleTarget;
    [Tooltip("카운트업할 플레이어 행의 뷰. 비어 있으면 자식에서 찾는다.")]
    [SerializeField] private ResultStatsView statsView;

    [Header("창 열기")]
    [Tooltip("열기 시간(초, unscaled). 0 이하면 즉시 다 보인다.")]
    [SerializeField] private float openDuration = 0.22f;
    [Tooltip("숨김 상태의 크기 배율 — 열 때 이 값에서 1 로, 닫을 때 1 에서 이 값으로.")]
    [SerializeField] private float hiddenScale = 0.95f;
    [Tooltip("열기 곡선(가로 = 진행 0~1, 세로 = 보임 0~1). 1 을 넘기면 크기가 살짝 튀었다 돌아온다(투명도는 1 에서 자름).")]
    [SerializeField] private AnimationCurve openCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));

    [Header("창 닫기")]
    [Tooltip("닫기 시간(초, unscaled). 0 이하면 즉시 꺼진다.")]
    [SerializeField] private float closeDuration = 0.14f;
    [Tooltip("닫기 곡선(가로 = 진행 0~1, 세로 = 숨김 정도 0~1 — 1 이면 다 사라짐).")]
    [SerializeField] private AnimationCurve closeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("숫자·시간 카운트업")]
    [Tooltip("열기 시작부터 숫자가 올라가기 시작할 때까지(초, unscaled). 그동안 숫자·시간은 0.")]
    [SerializeField] private float countUpDelay = 0.15f;
    [Tooltip("0 → 최종값까지 걸리는 시간(초, unscaled). 0 이하면 카운트업 없이 바로 최종값. 소요 시간(mm:ss)도 같은 진행도.")]
    [SerializeField] private float countUpDuration = 1.5f;
    [Tooltip("카운트업 곡선(가로 = 진행 0~1, 세로 = 최종값 대비 0~1). 0~1 밖은 자른다. 기본 = 강한 감속(30% 시간에 82%, 남은 70% 시간 동안 마지막 자리가 천천히 — 할 일 220). 숫자는 소수 버림이라 최종값은 끝(펀치 순간)에만 나온다.")]
    [SerializeField] private AnimationCurve countUpCurve = DefaultCountUpCurve();
    [Tooltip("값 크기별 감속 세기(할 일 220) — 최종값(소요 시간은 초 수)이 이 이하면 곡선 없이 선형(정수가 끝까지 고르게). 감속 꼬리 동안 정수가 안 바뀌어 멈춰 보이는 걸 막는다.")]
    [SerializeField] private float linearMaxSteps = 10f;
    [Tooltip("최종값이 이 이상이면 countUpCurve 그대로(극적 감속). 선형 기준과 사이는 자릿수(log10)로 섞는다 — 기본 10~1000 이면 100 = 반반. 모든 값은 같은 순간(펀치)에 최종값.")]
    [SerializeField] private float curveFullSteps = 1000f;

    [Header("카운트업 끝 패널 펀치 (EuniTween)")]
    [Tooltip("카운트업이 끝나는 순간(최종값 도달) 창 본체(scaleTarget)가 커졌다 돌아오는 시간(초, unscaled). 0 이하면 펀치 없음. 왕관 첫 점프는 펀치가 끝난 뒤.")]
    [SerializeField] private float punchDuration = 0.28f;
    [Tooltip("펀치 최대 크기 배율(1 = 펀치 없음). 열기 크기 위에 곱한다.")]
    [SerializeField] private float punchScale = 1.06f;
    [Tooltip("펀치 이징 — 커지는 절반에 적용, 줄어드는 절반은 거울(EuniTween Easing.Punch). OutQuad = 빠르게 커졌다 꼭대기에서 멈칫.")]
    [SerializeField] private Ease punchEase = Ease.OutQuad;

    [Header("등급 도장 '쾅' (할 일 223, EuniTween)")]
    [Tooltip("패널 펀치가 끝나고(펀치 없으면 카운트업 끝) 도장이 내려찍기 시작할 때까지(초, unscaled). 음수면 펀치와 겹치게 당긴다(카운트업 끝보다 앞은 안 됨). 도장(LobbyResultPanel.gradeStamp)이 없으면 이 칸들은 무시.")]
    [SerializeField] private float stampDelay = 0f;
    [Tooltip("내려찍기 시간(초, unscaled) — 크기 stampStartScale→1, 투명도 0→1, 기울기 시작→최종.")]
    [SerializeField] private float stampDropDuration = 0.12f;
    [Tooltip("내려찍기 시작 크기 배율.")]
    [SerializeField] private float stampStartScale = 2.5f;
    [Tooltip("내려찍기 시작 기울기(도, 화면 안 — 음수 = 시계 방향).")]
    [SerializeField] private float stampStartAngle = -12f;
    [Tooltip("최종 기울기(도). 연출 없이 열려도 이 기울기.")]
    [SerializeField] private float stampEndAngle = -8f;
    [Tooltip("내려찍기 이징(EuniTween) — InQuad = 끝에서 빨라져 '쾅'.")]
    [SerializeField] private Ease stampDropEase = Ease.InQuad;
    [Tooltip("착지 순간 창 본체(scaleTarget) 흔들림 시간(초, unscaled). 0 이하면 흔들림 없음.")]
    [SerializeField] private float stampShakeDuration = 0.12f;
    [Tooltip("흔들림 세기(UI px, 첫 순간 — 끝으로 갈수록 줄어든다).")]
    [SerializeField] private float stampShakeAmplitude = 8f;
    [Tooltip("흔들림 동안 좌우 왕복 횟수.")]
    [SerializeField] private float stampShakeCycles = 3f;
    [Tooltip("착지 반동(작게 튀었다 자리 잡기) 시간(초, unscaled). 0 이하면 반동 없음.")]
    [SerializeField] private float stampBounceDuration = 0.16f;
    [Tooltip("반동 최대 크기 배율(1 = 반동 없음).")]
    [SerializeField] private float stampBounceScale = 1.08f;
    [Tooltip("반동 이징 — 커지는 절반에 적용, 줄어드는 절반은 거울(EuniTween Easing.Punch).")]
    [SerializeField] private Ease stampBounceEase = Ease.OutQuad;
    [Tooltip("착지 순간 재생할 사운드 키(AudioManager). 비우면 무음.")]
    [SerializeField] private string stampSoundKey = "";

    [Header("1위 왕관 점프·회전 (EuniTween)")]
    [Tooltip("카운트업 끝 패널 펀치가 끝나고(펀치 없으면 카운트업 끝) 등급 도장이 자리 잡은 뒤(도장 없으면 펀치 끝) 왕관이 뛰기 시작할 때까지(초, unscaled).")]
    [SerializeField] private float crownDelay = 0.05f;
    [Tooltip("점프 + 한 바퀴 시간(초, unscaled). 0 이하면 왕관 연출 없음(반짝임만 착지 시각에).")]
    [SerializeField] private float crownDuration = 0.6f;
    [Tooltip("점프·회전 반복 주기(초, 시작→다음 시작, unscaled). 첫 점프는 펀치 직후, 그 뒤 이 간격마다. 0 이하면 1회만. 점프 시간보다 짧으면 점프 시간.")]
    [SerializeField] private float crownRepeatInterval = 3f;
    [Tooltip("점프 높이(UI 단위, 캔버스 기준 px).")]
    [SerializeField] private float crownJumpHeight = 28f;
    [Tooltip("점프 이징 — 오르는 절반에 적용, 내려오는 절반은 거울(EuniTween Easing.Punch). OutQuad = 포물선처럼 위에서 느려짐.")]
    [SerializeField] private Ease crownJumpEase = Ease.OutQuad;
    [Tooltip("회전 축(왕관 로컬). Y = 동전 뒤집듯 좌우로 한 바퀴(왕관이 늘 서 있음, 추천). Z = 화면 안에서 바퀴처럼(중간에 거꾸로 섬).")]
    [SerializeField] private Vector3 crownSpinAxis = Vector3.up;
    [Tooltip("회전 각도(도). 360 = 한 바퀴.")]
    [SerializeField] private float crownSpinDegrees = 360f;
    [Tooltip("회전 이징(EuniTween).")]
    [SerializeField] private Ease crownSpinEase = Ease.InOutCubic;

    [Header("1위 왕관 반짝임")]
    [Tooltip("반짝임 스프라이트 — 교체는 이 칸만. 비우면 반짝임 없음. 기본 = 기존 에셋 Flare06(4갈래 별빛, 50.Art/VFX/Textures).")]
    [SerializeField] private Sprite sparkleSprite;
    [Tooltip("반짝임 색(스프라이트 흰색에 곱한다).")]
    [SerializeField] private Color sparkleColor = new Color(1f, 0.93f, 0.62f, 1f);
    [Tooltip("반짝임 위치 — 왕관 크기 대비(0,0 = 가운데, ±0.5 = 가장자리). 개수 = 반짝임 수.")]
    [SerializeField] private Vector2[] sparkleOffsets = { new Vector2(0.38f, 0.34f), new Vector2(-0.36f, 0.08f), new Vector2(0.22f, -0.3f) };
    [Tooltip("반짝임 최대 크기 — 왕관 높이 대비.")]
    [SerializeField] private float sparkleSize = 0.55f;
    [Tooltip("반짝임 하나가 켜졌다 꺼지는 시간(초, unscaled). 0 이하면 반짝임 없음.")]
    [SerializeField] private float sparkleDuration = 0.4f;
    [Tooltip("착지 순간 몰아칠 때 반짝임 사이 간격(초).")]
    [SerializeField] private float sparkleStagger = 0.08f;
    [Tooltip("착지 몰아치기 뒤 은은한 반복 간격(초) — 하나씩 번갈아. 다음 착지에서 다시 몰아치기부터. 0 이하면 착지 때만. 점프 중엔 반짝임을 끈다.")]
    [SerializeField] private float sparkleRepeatInterval = 0.7f;
    [Tooltip("반복 반짝임 크기 배율(착지 때 = 1). 은은하게 = 1 보다 작게.")]
    [SerializeField] private float sparkleRepeatScale = 0.7f;
    [Tooltip("반짝임 하나가 켜졌다 꺼지는 동안 도는 각도(도, 화면 안).")]
    [SerializeField] private float sparkleSpinDegrees = 90f;
    [Tooltip("반짝임 크기·투명도 이징 — 커지는 절반에 적용, 줄어드는 절반은 거울(EuniTween Easing.Punch).")]
    [SerializeField] private Ease sparkleEase = Ease.OutCubic;

    private CanvasGroup _canvasGroup;
    private UiModalBlocker _blocker;
    private LobbyResultPanel _panel;
    private bool _resolved;

    private float _visibility;     // 0 = 숨김, 1 = 다 보임
    private bool _tweening;
    private bool _opening;
    private float _tweenFrom;
    private float _tweenElapsed;
    private Action _onHidden;

    private bool _countingUp;
    private bool _punching;
    private float _countUpElapsed; // 열기부터(초, unscaled) — 카운트업·펀치 공용
    private float _punchMultiplier = 1f;

    private RectTransform _stamp;          // 등급 도장(할 일 223) — 패널 소유, 없으면 null
    private CanvasGroup _stampGroup;
    private Vector3 _stampBaseScale = Vector3.one;
    private Vector2 _shakeBasePosition;    // 창 본체 원래 위치(흔들림 기준)
    private bool _stamping;
    private bool _stampLanded;

    private RectTransform _crown;  // 이번 열기의 1위 왕관(행이 다시 그려지면 파괴된다 — Unity null 로 확인)
    private Vector2 _crownBasePosition;
    private Quaternion _crownBaseRotation;
    private bool _crownPlaying;
    private float _crownElapsed;   // 열기부터(초, unscaled)
    private readonly List<Image> _sparkles = new List<Image>();

    /// <summary>닫힘 연출 중인가(Content 는 아직 켜져 있다).</summary>
    public bool IsClosing => _tweening && !_opening;

    /// <summary>
    /// 연다 — <c>Content</c> 를 켜고 내용을 채운 <b>뒤에</b> 부른다. 닫히는 중이면 지금 보임 정도에서 이어서 연다.
    /// 숫자·시간 카운트업과 왕관 연출은 처음부터 다시(행은 막 다시 그려졌다).
    /// </summary>
    public void PlayOpen()
    {
        Resolve();
        _onHidden = null;

        if (_blocker != null)
            _blocker.enabled = true;
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;

        StartTween(opening: true);

        _countUpElapsed = 0f;
        _countingUp = countUpDuration > 0f && (statsView != null || _panel != null);
        _punching = _countingUp && PunchEnabled; // 카운트업이 없으면 "끝나는 순간"도 없다
        _punchMultiplier = 1f;
        ApplyCountUp();
        ApplyPunch();
        StartStamp();

        StartCrown();
    }

    /// <summary>
    /// 닫는다. 연출이 끝나면 <paramref name="onHidden"/>(= Content 끄기)을 한 번 부른다.
    /// 꺼져 있거나 닫기 시간이 0 이하면 즉시 부른다. 그 전에 <see cref="PlayOpen"/> 이 오면 부르지 않는다.
    /// </summary>
    public void PlayClose(Action onHidden)
    {
        Resolve();

        // 입력은 닫기 시작과 동시에 돌려준다(보이는 건 아직 사라지는 중).
        if (_blocker != null)
            _blocker.enabled = false;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
        StopPunch(); // 닫힘 크기 연출이 바로 이어받는다
        StopStamp(); // 흔들림은 제자리로, 도장은 최종 모습으로

        if (!isActiveAndEnabled || closeDuration <= 0f)
        {
            FinishHidden();
            onHidden?.Invoke();
            return;
        }

        _onHidden = onHidden;
        StartTween(opening: false);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (_countingUp || _punching || _stamping)
        {
            _countUpElapsed += dt;
            if (_countingUp)
                ApplyCountUp();
            if (_punching)
                ApplyPunch();
            if (_stamping)
                ApplyStamp();
        }

        if (_crownPlaying)
        {
            _crownElapsed += dt;
            ApplyCrown();
        }

        if (!_tweening)
            return;

        _tweenElapsed += dt;
        float duration = _opening ? openDuration : closeDuration;
        float progress = LobbyResultPanelAnimMath.Progress(_tweenElapsed, duration);
        AnimationCurve curve = _opening ? openCurve : closeCurve;
        float curved = curve != null ? curve.Evaluate(progress) : progress;
        SetVisibility(Mathf.LerpUnclamped(_tweenFrom, _opening ? 1f : 0f, curved));

        if (progress < 1f)
            return;

        _tweening = false;
        if (_opening)
        {
            SetVisibility(1f); // 곡선 끝값이 1 이 아니어도 정확히 다 보이게
            return;
        }

        Action onHidden = _onHidden;
        FinishHidden();
        onHidden?.Invoke();
    }

    private void OnDisable()
    {
        // 연출 도중 Content 가 밖에서 꺼졌다 — 다음 열기는 숨김 상태에서 시작한다. 콜백은 버린다(이미 꺼졌다).
        if (_resolved)
            FinishHidden();
    }

    private void StartTween(bool opening)
    {
        _opening = opening;
        _tweenFrom = _visibility;
        _tweenElapsed = 0f;
        _tweening = true;

        float duration = opening ? openDuration : closeDuration;
        if (duration <= 0f)
        {
            _tweening = false;
            SetVisibility(opening ? 1f : 0f);
            return;
        }

        SetVisibility(_visibility); // 첫 프레임부터 시작값으로(열 때 다 보인 채 한 프레임 깜빡이지 않게)
    }

    private void FinishHidden()
    {
        _tweening = false;
        _countingUp = false;
        _onHidden = null;
        StopPunch();
        StopStamp();
        StopCrown();
        SetVisibility(0f);
    }

    private void ApplyCountUp()
    {
        float progress = LobbyResultPanelAnimMath.CountUpProgress(_countUpElapsed, countUpDelay, countUpDuration);
        float curved = progress >= 1f ? 1f : (countUpCurve != null ? countUpCurve.Evaluate(progress) : progress);
        // 값마다 크기에 맞춰 선형↔곡선을 섞는다(작은 값 = 고르게, 큰 값 = 극적 감속). 진행도 1 에서 모두 동시에 최종값.
        var frame = new LobbyResultCountUpFrame(progress, Mathf.Clamp01(curved), linearMaxSteps, curveFullSteps);
        if (statsView != null)
            statsView.SetCountUpProgress(frame);
        if (_panel != null)
            _panel.SetPlayTimeCountUp(frame);

        if (progress >= 1f)
            _countingUp = false;
    }

    // ── 1위 왕관 ──────────────────────────────────────────────

    private void StartCrown()
    {
        StopCrown(); // 지난 열기의 왕관이 아직 살아 있으면 제자리로

        _crown = statsView != null ? statsView.FindVisibleCrown() : null;
        if (_crown == null)
            return;

        _crownBasePosition = _crown.anchoredPosition;
        _crownBaseRotation = _crown.localRotation;
        _crownElapsed = 0f;
        _crownPlaying = true;
        ApplyCrown();
    }

    private void ApplyCrown()
    {
        if (_crown == null) // 행이 다시 그려져 왕관이 파괴됐다
        {
            _crownPlaying = false;
            _sparkles.Clear();
            return;
        }

        float firstStart = LobbyResultPanelAnimMath.CrownFirstStart(countUpDelay, countUpDuration, BeforeCrownDuration, crownDelay);
        float progress = LobbyResultPanelAnimMath.CrownCycleProgress(_crownElapsed, firstStart, crownDuration, crownRepeatInterval);
        float jump = LobbyResultPanelAnimMath.CrownJumpOffset(progress, crownJumpHeight, crownJumpEase);
        float angle = LobbyResultPanelAnimMath.CrownSpinAngle(progress, crownSpinDegrees, crownSpinEase);
        _crown.anchoredPosition = _crownBasePosition + new Vector2(0f, jump);
        // 한 바퀴 끝(360°)은 제자리 모습 — 부동소수 찌꺼기 없이 원래 회전으로 맞춘다.
        bool jumping = progress > 0f && progress < 1f;
        _crown.localRotation = !jumping || crownSpinAxis == Vector3.zero
            ? _crownBaseRotation
            : _crownBaseRotation * Quaternion.AngleAxis(angle, crownSpinAxis);

        // 반짝임은 착지 기준 — 착지마다 크게 한 번 + 사이엔 은은한 반복. 점프 중엔 끈다(Y 회전에 같이 납작해지지 않게).
        float sinceLand = LobbyResultPanelAnimMath.SinceLastCrownLand(_crownElapsed, firstStart, crownDuration, crownRepeatInterval);
        if (jumping)
            HideSparkles();
        else
            ApplySparkles(sinceLand);

        // 반복 점프가 없을 때만 멈춘다: 점프가 끝났고 반짝임도(반복 반짝임이 없으면) 다 끝났다. 반복이 있으면 열려 있는 동안 계속.
        if (crownRepeatInterval > 0f || progress < 1f)
            return;
        int sparkleCount = sparkleOffsets != null ? sparkleOffsets.Length : 0;
        bool sparkleLoops = sparkleRepeatInterval > 0f && sparkleSprite != null && sparkleCount > 0 && sparkleDuration > 0f;
        if (!sparkleLoops && sinceLand >= Mathf.Max(0f, sparkleStagger) * sparkleCount + Mathf.Max(0f, sparkleDuration))
        {
            _crownPlaying = false;
            HideSparkles();
        }
    }

    private void ApplySparkles(float sinceLand)
    {
        int count = sparkleOffsets != null ? sparkleOffsets.Length : 0;
        if (sparkleSprite == null || count == 0 || sparkleDuration <= 0f || sinceLand < 0f)
            return;

        EnsureSparkles(count);
        Vector2 crownSize = _crown.rect.size;
        for (int i = 0; i < count; i++)
        {
            Image image = _sparkles[i];
            float t = LobbyResultPanelAnimMath.SparkleProgress(sinceLand, i, count, sparkleStagger, sparkleDuration, sparkleRepeatInterval);
            float pulse = LobbyResultPanelAnimMath.SparklePulse(t, sparkleEase);
            if (pulse <= 0f)
            {
                image.enabled = false;
                continue;
            }

            // 착지 몰아치기(첫 회차)는 크게, 반복은 은은하게.
            bool burst = sinceLand < Mathf.Max(0f, sparkleStagger) * (count - 1) + sparkleDuration;
            float size = crownSize.y * sparkleSize * pulse * (burst ? 1f : sparkleRepeatScale);
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = Vector2.Scale(sparkleOffsets[i], crownSize);
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.Euler(0f, 0f, -sparkleSpinDegrees * t);
            Color color = sparkleColor;
            color.a *= pulse;
            image.color = color;
            image.enabled = true;
        }
    }

    private void EnsureSparkles(int count)
    {
        // 왕관이 바뀌었으면(행 재생성) 예전 Image 는 왕관과 함께 파괴됐다 — 목록을 비우고 새로 만든다.
        if (_sparkles.Count > 0 && (_sparkles[0] == null || _sparkles[0].transform.parent != _crown))
            _sparkles.Clear();

        while (_sparkles.Count < count)
        {
            var go = new GameObject($"Sparkle_{_sparkles.Count}", typeof(RectTransform), typeof(Image));
            go.layer = _crown.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(_crown, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            Image image = go.GetComponent<Image>();
            image.sprite = sparkleSprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;
            _sparkles.Add(image);
        }
    }

    private void HideSparkles()
    {
        for (int i = 0; i < _sparkles.Count; i++)
        {
            if (_sparkles[i] != null)
                _sparkles[i].enabled = false;
        }
    }

    private void StopCrown()
    {
        _crownPlaying = false;
        HideSparkles();
        if (_crown != null)
        {
            _crown.anchoredPosition = _crownBasePosition;
            _crown.localRotation = _crownBaseRotation;
        }
        _crown = null;
        _sparkles.Clear(); // Image 는 왕관 자식이라 행이 다시 그려질 때 같이 사라진다
    }

    // ── 등급 도장 '쾅'(할 일 223) ──────────────────────────────

    /// <summary>도장이 있고 카운트업(= 찍힐 순간)이 있을 때만 내려찍는다. 아니면 처음부터 최종 모습.</summary>
    private bool StampAnimates => _stamp != null && _stamp.gameObject.activeSelf && countUpDuration > 0f;

    /// <summary>왕관 첫 점프 전에 기다릴 시간(카운트업 끝부터) = 펀치 + 도장(지연 + 내려찍기 + 흔들림·반동). 겹치지 않게 차례로.</summary>
    private float BeforeCrownDuration
    {
        get
        {
            float punch = PunchEnabled ? punchDuration : 0f;
            if (!StampAnimates)
                return punch;
            float stampStart = LobbyResultPanelAnimMath.StampStart(0f, 0f, punch, stampDelay);
            return Mathf.Max(punch, stampStart + LobbyResultPanelAnimMath.StampTotal(stampDropDuration, stampShakeDuration, stampBounceDuration));
        }
    }

    private void StartStamp()
    {
        StopStamp();
        if (_stamp == null)
            return;

        _stampLanded = false;
        _stamping = StampAnimates;
        if (_stamping)
            ApplyStamp(); // 첫 프레임부터 숨김(크게·투명)
        else
            ApplyStampPose(new LobbyResultStampPose(1f, 1f, stampEndAngle), 1f);
    }

    private void ApplyStamp()
    {
        if (_stamp == null)
        {
            _stamping = false;
            return;
        }

        float start = LobbyResultPanelAnimMath.StampStart(countUpDelay, countUpDuration, PunchEnabled ? punchDuration : 0f, stampDelay);
        float since = _countUpElapsed - start;
        float sinceLand = since - Mathf.Max(0f, stampDropDuration);

        LobbyResultStampPose pose = LobbyResultPanelAnimMath.StampDropPose(since, stampDropDuration, stampStartScale, stampStartAngle, stampEndAngle, stampDropEase);
        float bounce = LobbyResultPanelAnimMath.StampBounceScale(sinceLand, stampBounceDuration, stampBounceScale, stampBounceEase);
        ApplyStampPose(pose, bounce);

        if (scaleTarget != null)
            scaleTarget.anchoredPosition = _shakeBasePosition +
                LobbyResultPanelAnimMath.StampShakeOffset(sinceLand, stampShakeDuration, stampShakeAmplitude, stampShakeCycles);

        if (!_stampLanded && sinceLand >= 0f)
        {
            _stampLanded = true;
            if (!string.IsNullOrEmpty(stampSoundKey) && AudioManager.Instance != null)
                AudioManager.Instance.Play(stampSoundKey);
        }

        if (sinceLand >= Mathf.Max(0f, Mathf.Max(stampShakeDuration, stampBounceDuration)))
            StopStamp();
    }

    private void ApplyStampPose(LobbyResultStampPose pose, float bounce)
    {
        float scale = pose.Scale * bounce;
        _stamp.localScale = new Vector3(_stampBaseScale.x * scale, _stampBaseScale.y * scale, _stampBaseScale.z);
        _stamp.localRotation = Quaternion.Euler(0f, 0f, pose.Angle);
        if (_stampGroup != null)
            _stampGroup.alpha = pose.Alpha;
    }

    /// <summary>멈춤 — 창 본체는 제자리, 도장은 최종 모습(닫힘 연출 중에도 찍힌 채 사라진다).</summary>
    private void StopStamp()
    {
        bool wasStamping = _stamping;
        _stamping = false;
        if (wasStamping && scaleTarget != null)
            scaleTarget.anchoredPosition = _shakeBasePosition;
        if (_stamp != null)
            ApplyStampPose(new LobbyResultStampPose(1f, 1f, stampEndAngle), 1f);
    }

    // ── 카운트업 끝 패널 펀치(할 일 220) ─────────────────────────

    private bool PunchEnabled => punchDuration > 0f && !Mathf.Approximately(punchScale, 1f);

    private void ApplyPunch()
    {
        float since = _countUpElapsed - LobbyResultPanelAnimMath.CountUpEnd(countUpDelay, countUpDuration);
        _punchMultiplier = LobbyResultPanelAnimMath.PanelPunchScale(since, punchDuration, punchScale, punchEase);
        if (since >= punchDuration)
        {
            _punching = false;
            _punchMultiplier = 1f;
        }
        ApplyScale();
    }

    private void StopPunch()
    {
        _punching = false;
        _punchMultiplier = 1f;
        ApplyScale();
    }

    private void SetVisibility(float visibility)
    {
        _visibility = visibility;
        if (_canvasGroup != null)
            _canvasGroup.alpha = LobbyResultPanelAnimMath.Alpha(visibility);
        ApplyScale();
    }

    // 크기 = 열기·닫기(보임 정도) × 카운트업 끝 펀치. 둘이 시간상 겹쳐도 곱이라 튀지 않는다.
    private void ApplyScale()
    {
        if (scaleTarget == null)
            return;
        float scale = LobbyResultPanelAnimMath.Scale(_visibility, hiddenScale) * _punchMultiplier;
        scaleTarget.localScale = new Vector3(scale, scale, 1f);
    }

    // 강한 감속: 0~0.3 에 0→0.82(시작 기울기 4.5), 0.3~1 에 0.82→1(기울기 0.5→0 — 넘침 없음, 끝에서 거의 멈춤).
    /// <summary>카운트업 곡선 기본값(할 일 220) — 인스펙터 기본·테스트 공용. 프리팹에는 같은 키가 저장돼 있다.</summary>
    public static AnimationCurve DefaultCountUpCurve() => new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 4.5f),
        new Keyframe(0.3f, 0.82f, 0.5f, 0.5f),
        new Keyframe(1f, 1f, 0f, 0f));

    private void Resolve()
    {
        if (_resolved)
            return;

        _resolved = true;
        // ??= 는 Unity 의 가짜 null 을 못 거른다 — 명시적으로 == null 비교한다.
        if (scaleTarget == null)
            scaleTarget = transform.Find("Panel") as RectTransform;
        if (statsView == null)
            statsView = GetComponentInChildren<ResultStatsView>(true);
        _panel = GetComponentInParent<LobbyResultPanel>(true); // 소요 시간 카운트업(텍스트는 패널 소유)
        if (scaleTarget != null)
            _shakeBasePosition = scaleTarget.anchoredPosition;
        _stamp = _panel != null ? _panel.GradeStamp : null; // 등급 도장(할 일 223)
        if (_stamp != null)
        {
            _stampBaseScale = _stamp.localScale;
            _stampGroup = _stamp.GetComponent<CanvasGroup>();
            if (_stampGroup == null)
                _stampGroup = _stamp.gameObject.AddComponent<CanvasGroup>();
            _stampGroup.interactable = false;
            _stampGroup.blocksRaycasts = false;
        }
        _blocker = GetComponent<UiModalBlocker>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }
}
