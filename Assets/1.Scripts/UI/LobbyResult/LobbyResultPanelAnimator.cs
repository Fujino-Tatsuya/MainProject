using System;
using UnityEngine;

/// <summary>
/// 로비 결과창 애니메이션(할 일 215) — 표시 전용. <see cref="LobbyResultPanel"/> 의 <c>Content</c> 에 붙는다.
/// (a) 창 등장: 열 때 크기(<c>hiddenScale</c>→1)·투명도(0→1), 닫을 때 반대(짧게).
/// (c) 숫자 카운트업: 열 때 플레이어 행 숫자가 0 에서 최종값까지 올라간다(<see cref="ResultStatsView.SetCountUpProgress"/>).
///
/// - 시간 = <c>Time.unscaledDeltaTime</c>(슬로모션 영향 없음). 곡선·지속시간 = 이 컴포넌트 인스펙터.
/// - 열고 닫기 판단·<c>Closed</c> 이벤트·ESC 점유는 패널이 그대로 한다. 이 컴포넌트는 닫힘 연출이 끝난 뒤
///   패널이 넘긴 콜백(= <c>Content</c> 끄기)만 늦게 부른다. 이 컴포넌트가 없으면 패널은 예전처럼 즉시 켜고 끈다.
/// - 닫힘 연출이 시작되면 바로 입력을 돌려준다: <see cref="UiModalBlocker"/> 를 끄고 CanvasGroup 의 클릭 막기를 푼다.
/// - CanvasGroup 은 프리팹에 따로 두지 않는다 — 없으면 런타임에 붙인다(프리팹 변경 = 이 컴포넌트 하나).
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

    [Header("숫자 카운트업")]
    [Tooltip("열기 시작부터 숫자가 올라가기 시작할 때까지(초, unscaled). 그동안 숫자는 0.")]
    [SerializeField] private float countUpDelay = 0.15f;
    [Tooltip("0 → 최종값까지 걸리는 시간(초, unscaled). 0 이하면 카운트업 없이 바로 최종값.")]
    [SerializeField] private float countUpDuration = 0.8f;
    [Tooltip("카운트업 곡선(가로 = 진행 0~1, 세로 = 최종값 대비 0~1). 0~1 밖은 자른다.")]
    [SerializeField] private AnimationCurve countUpCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));

    private CanvasGroup _canvasGroup;
    private UiModalBlocker _blocker;
    private bool _resolved;

    private float _visibility;     // 0 = 숨김, 1 = 다 보임
    private bool _tweening;
    private bool _opening;
    private float _tweenFrom;
    private float _tweenElapsed;
    private Action _onHidden;

    private bool _countingUp;
    private float _countUpElapsed;

    /// <summary>닫힘 연출 중인가(Content 는 아직 켜져 있다).</summary>
    public bool IsClosing => _tweening && !_opening;

    /// <summary>
    /// 연다 — <c>Content</c> 를 켜고 내용을 채운 <b>뒤에</b> 부른다. 닫히는 중이면 지금 보임 정도에서 이어서 연다.
    /// 숫자 카운트업은 처음부터 다시(행은 막 다시 그려졌다).
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
        _countingUp = countUpDuration > 0f && statsView != null;
        ApplyCountUp();
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
        SetVisibility(0f);
    }

    private void ApplyCountUp()
    {
        if (statsView == null)
            return;

        float progress = LobbyResultPanelAnimMath.CountUpProgress(_countUpElapsed, countUpDelay, countUpDuration);
        float curved = progress >= 1f ? 1f : (countUpCurve != null ? countUpCurve.Evaluate(progress) : progress);
        statsView.SetCountUpProgress(Mathf.Clamp01(curved));

        if (progress >= 1f)
            _countingUp = false;
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
        _blocker = GetComponent<UiModalBlocker>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }
}
