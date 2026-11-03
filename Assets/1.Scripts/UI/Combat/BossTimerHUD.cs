using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 제한시간 게이지(표시 전용). 네트워크를 직접 만지지 않고
/// <see cref="BossTimerManager"/> 의 복제된 상태만 읽는다 — 값의 주인은 서버다.
///
/// 아트 3종(<c>slot_timer</c> 틀 / <c>gauge_timer</c> 채움 / <c>icon_timer_boss</c> 해골 캡)은
/// <c>Tools/UI/Authoring/CombatHUD — 미니맵·보스타이머 슬롯 생성</c> 이 배선한다.
///
/// 🔴 게이지 방향은 <see cref="BossTimerManager"/> 의 인스펙터 토글이 정한다(기획 미확정).
///    여기서는 <c>Fill01</c> 을 그대로 그린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossTimerHUD : MonoBehaviour
{
    [Tooltip("채움 이미지. Image Type = Filled / Horizontal 이어야 한다.")]
    [SerializeField] private Image fillImage;

    [Tooltip("타이머 위젯 전체의 표시를 제어한다. 보스 타이머가 없는 씬에서는 투명하게 둔다. " +
             "🔴 GameObject 를 끄지 않는다 — 자기 자신을 끄면 Update 가 멈춰 되살아날 수 없다.")]
    [SerializeField] private CanvasGroup group;

    [Tooltip("표시 갱신 주기(초). 게이지는 1초에 몇 번만 갱신해도 충분하다.")]
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

    [Header("해골 아이콘 점멸 (할 일 166)")]
    [Tooltip("점멸할 해골 아이콘. 비어 있으면 자식 'BossIcon' 을 이름으로 찾는다. 알파는 Image.color.a 만 바꾼다.")]
    [SerializeField] private Image bossIcon;

    [Tooltip("타이머 시작 순간의 점멸 주기(초). 경과 비율에 EaseInExpo 를 걸어 최소 주기로 줄어든다.")]
    [SerializeField, Min(0.02f), DataTableIgnore] private float blinkStartPeriod = 10f;

    [Tooltip("만료 직전의 점멸 주기(초).")]
    [SerializeField, Min(0.02f), DataTableIgnore] private float blinkMinPeriod = 0.2f;

    [Tooltip("점멸 중 가장 어두울 때의 아이콘 알파(1 과 이 값 사이를 부드럽게 오간다).")]
    [SerializeField, Range(0f, 1f), DataTableIgnore] private float blinkMinAlpha = 0.25f;

    private float _timer;
    private float _blinkPhase;

    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        if (fillImage == null) fillImage = GetComponentInChildren<Image>(true);
        if (bossIcon == null)
        {
            Transform icon = transform.Find("BossIcon");
            if (icon != null) bossIcon = icon.GetComponent<Image>();
        }
        Apply();
    }

    private void OnEnable()
    {
        _timer = 0f;
        Apply();
    }

    private void OnDisable() => ResetBlink();

    private void Update()
    {
        // 점멸은 매 프레임(게이지 갱신 주기와 분리). 게임 시간이라 간파 슬로우 때 같이 느려진다.
        UpdateBlink(Time.deltaTime);

        // 갱신 주기만 게임 시간으로 센다(D9). 남은 시간 자체는 BossTimerManager 가 GameNow(슬로우·일시정지 제외)로 계산한다.
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        _timer = refreshInterval;
        Apply();
    }

    private void UpdateBlink(float dt)
    {
        BossTimerManager timer = BossTimerManager.Instance;
        bool blinking = timer != null && timer.isActiveAndEnabled
                        && timer.State == BossTimerManager.TimerState.Running
                        && group != null && group.alpha > 0f;
        if (!blinking)
        {
            ResetBlink();
            return;
        }

        // 주기가 바뀌어도 튀지 않게 위상을 누적한다.
        float t = ElapsedFraction(timer.RemainingSeconds, timer.LimitSeconds);
        _blinkPhase = Mathf.Repeat(_blinkPhase + dt / BlinkPeriod(t, blinkStartPeriod, blinkMinPeriod), 1f);
        SetIconAlpha(BlinkAlpha(_blinkPhase, blinkMinAlpha));
    }

    private void ResetBlink()
    {
        _blinkPhase = 0f;
        SetIconAlpha(1f);
    }

    private void SetIconAlpha(float a)
    {
        if (bossIcon == null) return;
        Color c = bossIcon.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        bossIcon.color = c;
    }

    // ---- 순수 계산(EditMode 테스트 대상) ----

    /// <summary>EaseInExpo: t==0 → 0, 그 외 2^(10t-10). 처음엔 거의 0, 끝에서 급격히 1.</summary>
    public static float EaseInExpo(float t) => t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);

    /// <summary>경과 비율 0(시작)~1(만료). 제한시간 ≤ 0 이면 1.</summary>
    public static float ElapsedFraction(float remainingSeconds, float limitSeconds) =>
        limitSeconds <= 0f ? 1f : Mathf.Clamp01(1f - remainingSeconds / limitSeconds);

    /// <summary>점멸 주기(초) = Lerp(시작 주기, 최소 주기, EaseInExpo(t)).</summary>
    public static float BlinkPeriod(float t, float startPeriod, float minPeriod) =>
        Mathf.Lerp(startPeriod, minPeriod, EaseInExpo(Mathf.Clamp01(t)));

    /// <summary>위상 0~1 → 알파. 위상 0 = 1(밝음), 0.5 = 최저 알파. 0.5-0.5cos 곡선.</summary>
    public static float BlinkAlpha(float phase01, float minAlpha)
    {
        float dark = 0.5f - 0.5f * Mathf.Cos(phase01 * 2f * Mathf.PI);
        return Mathf.Lerp(1f, minAlpha, dark);
    }

    private void Apply()
    {
        BossTimerManager timer = BossTimerManager.Instance;

        // 보스가 없는 씬(로비·테스트 씬)에서는 위젯을 감춘다 — 빈 틀만 떠 있으면 버그로 읽힌다.
        //
        // 🔴 예전에는 `root.SetActive(false)` 였는데, 저작 스크립트가 `root` 를 **이 컴포넌트 자신의
        //    GameObject** 로 배선한다. 초기화 순서상 Awake 시점에 매니저가 아직 없으면 스스로를 꺼버리고,
        //    그러면 Update 가 돌지 않아 **매니저가 나중에 생겨도 영원히 되살아나지 못했다.**
        //    알파로 감추면 컴포넌트는 계속 살아 있어 복구된다.
        // 튜토리얼(S5): 타이머 시작 전(Idle)에는 디렉터가 '타이머 HUD 표시'를 켤 때(Z4-0)까지 감춘다. 비튜토리얼은 기존대로.
        bool available = timer != null && timer.isActiveAndEnabled && !MapSceneBossPath.ShouldHideTimerHud(timer.State);
        if (group != null)
        {
            group.alpha = available ? 1f : 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        if (!available || fillImage == null)
            return;

        fillImage.fillAmount = Mathf.Clamp01(timer.Fill01);
    }
}
