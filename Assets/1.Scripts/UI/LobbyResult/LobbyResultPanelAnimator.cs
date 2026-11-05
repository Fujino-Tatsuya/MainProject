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
/// (d) 1위 왕관: 카운트업이 끝난 직후 점프하며 한 바퀴(기본 Y축) 돌고, 착지 순간 반짝임이 터진 뒤 은은하게 반복한다(EuniTween 이징).
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
    [SerializeField] private float countUpDuration = 0.8f;
    [Tooltip("카운트업 곡선(가로 = 진행 0~1, 세로 = 최종값 대비 0~1). 0~1 밖은 자른다.")]
    [SerializeField] private AnimationCurve countUpCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));

    [Header("1위 왕관 점프·회전 (EuniTween)")]
    [Tooltip("카운트업이 끝나고 왕관이 뛰기 시작할 때까지(초, unscaled).")]
    [SerializeField] private float crownDelay = 0.05f;
    [Tooltip("점프 + 한 바퀴 시간(초, unscaled). 0 이하면 왕관 연출 없음(반짝임만 착지 시각에).")]
    [SerializeField] private float crownDuration = 0.6f;
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
    [Tooltip("그 뒤 은은한 반복 간격(초) — 하나씩 번갈아. 0 이하면 착지 때 한 번만.")]
    [SerializeField] private float sparkleRepeatInterval = 1.6f;
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
    private float _countUpElapsed;

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
        ApplyCountUp();

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

        if (_countingUp)
        {
            _countUpElapsed += dt;
            ApplyCountUp();
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
        StopCrown();
        SetVisibility(0f);
    }

    private void ApplyCountUp()
    {
        float progress = LobbyResultPanelAnimMath.CountUpProgress(_countUpElapsed, countUpDelay, countUpDuration);
        float curved = progress >= 1f ? 1f : (countUpCurve != null ? countUpCurve.Evaluate(progress) : progress);
        curved = Mathf.Clamp01(curved);
        if (statsView != null)
            statsView.SetCountUpProgress(curved);
        if (_panel != null)
            _panel.SetPlayTimeCountUp(curved);

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

        float progress = crownDuration > 0f
            ? LobbyResultPanelAnimMath.CrownProgress(_crownElapsed, countUpDelay, countUpDuration, crownDelay, crownDuration)
            : 1f;
        float jump = LobbyResultPanelAnimMath.CrownJumpOffset(progress, crownJumpHeight, crownJumpEase);
        float angle = LobbyResultPanelAnimMath.CrownSpinAngle(progress, crownSpinDegrees, crownSpinEase);
        _crown.anchoredPosition = _crownBasePosition + new Vector2(0f, jump);
        // 한 바퀴 끝(360°)은 제자리 모습 — 부동소수 찌꺼기 없이 원래 회전으로 맞춘다.
        _crown.localRotation = progress >= 1f || crownSpinAxis == Vector3.zero
            ? _crownBaseRotation
            : _crownBaseRotation * Quaternion.AngleAxis(angle, crownSpinAxis);

        float landTime = LobbyResultPanelAnimMath.CrownLandTime(countUpDelay, countUpDuration, crownDelay, crownDuration);
        ApplySparkles(_crownElapsed - landTime);

        // 반복 반짝임이 없으면 다 끝난 뒤 멈춘다(반복이 있으면 열려 있는 동안 계속).
        int sparkleCount = sparkleOffsets != null ? sparkleOffsets.Length : 0;
        if (progress >= 1f && (sparkleRepeatInterval <= 0f || sparkleSprite == null || sparkleCount == 0)
            && _crownElapsed - landTime >= Mathf.Max(0f, sparkleStagger) * sparkleCount + Mathf.Max(0f, sparkleDuration))
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

    private void SetVisibility(float visibility)
    {
        _visibility = visibility;
        if (_canvasGroup != null)
            _canvasGroup.alpha = LobbyResultPanelAnimMath.Alpha(visibility);
        if (scaleTarget != null)
        {
            float scale = LobbyResultPanelAnimMath.Scale(visibility, hiddenScale);
            scaleTarget.localScale = new Vector3(scale, scale, 1f);
        }
    }

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
        _blocker = GetComponent<UiModalBlocker>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }
}
