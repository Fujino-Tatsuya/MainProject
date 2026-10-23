using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 상단 보스 체력바. BossHudTarget 등록 목록을 매 프레임 폴링해
/// 먼저 스폰된 생존 보스의 HP를 표시하고, 보스가 없으면 바 전체를 숨긴다.
/// (복수 보스 동시 표시는 필요 시 확장)
/// </summary>
public class BossHealthHUD : MonoBehaviour
{
    [SerializeField] private GameObject barRoot;
    [SerializeField] private Image hpFill;
    [Tooltip("HP 바 밑 회색 **간파 게이지**(Detection_Fill). 100% → 0% 로 HP 와 같은 방향으로 준다(팀 기획 09-28).\n" +
             "간파 게이지가 없는 보스면 숨긴다.")]
    [SerializeField] private Image counterGaugeFill;
    [SerializeField] private DelayedHealthBar delayed = new DelayedHealthBar();

    [Header("23호 레이드 ④ 자폭 실드(팀장 10-19)")]
    [Tooltip("HP 바 위를 덮는 실드 바(프리팹의 SelfDestructShieldFill — HP_Fill 바로 다음 형제). 비우면 실드를 표시하지 않는다.\n" +
             "자폭 시작 때 바 전체를 덮고, 실드가 깎일수록 오른쪽부터 줄어든다. 자폭이 아니면 숨긴다.")]
    [SerializeField] private Image shieldFill;
    [Tooltip("실드 바 색. Awake 에서 shieldFill 에 적용한다(색 원본은 이 필드).")]
    [SerializeField] private Color shieldColor = new Color(0.55f, 0.85f, 1f, 0.65f);

    [Header("등장 차오름(경석 10-23)")]
    [Tooltip("바가 처음 보일 때 이 비율에서 실제 HP 까지 빠르게 채운다. 끄면 처음부터 실제 값.")]
    [SerializeField] private bool introFillEnabled = true;
    [Tooltip("차오르기 시작 비율(0~1). 1/4 = 0.25.")]
    [SerializeField, Range(0f, 1f)] private float introFillStart = 0.25f;
    [Tooltip("차오르는 시간(초).")]
    [SerializeField, Min(0.05f)] private float introFillSeconds = 0.8f;

    private readonly BossHealthIntroFill introFill = new BossHealthIntroFill();
    private Unit introPlayedFor;              // 이 보스로 차오름을 한 번 했다(재도전 = 새 보스 인스턴스라 다시 한다)
    private bool wasShown;

    private Unit boundBoss;
    private TwentyThreeBoss boundGaugeBoss;   // 간파 게이지를 가진 보스(23호)일 때만

    private void Awake()
    {
        if (shieldFill != null)
            shieldFill.color = shieldColor;
        else
            Debug.LogWarning($"[BossHealthHUD] shieldFill 이 비어 있어 자폭 실드를 표시하지 않는다: {name}", this);
    }

    private void Update()
    {
        Unit boss = FindBoss();
        if (boss != boundBoss)
            BindBoss(boss);

        // 등장 시네마틱 워크인·강하·착지 동안은 숨긴다 — 웰즈 지시 구간부터 보인다(PLAN-boss-encounter-cutscene 정정 8).
        BossEncounterDirector director = BossEncounterDirector.Instance;
        bool introHides = director != null && director.IsSpawned && director.HidesBossHud;
        bool shouldShow = boss != null && boss.CurrentHealth > 0 && !introHides;
        if (barRoot != null && barRoot.activeSelf != shouldShow)
            barRoot.SetActive(shouldShow);

        if (!shouldShow)
        {
            wasShown = false;
            introFill.Cancel();
            return;
        }

        // 처음 보이는 순간(등장 연출이 끝나 바가 뜰 때) 한 번 — 1/4 에서 최대까지 빠르게 차오른다.
        if (!wasShown && introFillEnabled && introPlayedFor != boss)
        {
            introPlayedFor = boss;
            introFill.Begin();
        }
        wasShown = true;

        int maxHp = boss.FinalMaxHp;
        float actual01 = maxHp > 0 ? Mathf.Clamp01((float)boss.CurrentHealth / maxHp) : 0f;
        float shown01 = introFill.Evaluate(actual01, Time.unscaledDeltaTime, introFillStart, introFillSeconds);
        if (hpFill != null)
            hpFill.fillAmount = shown01;

        delayed.Tick(Time.deltaTime, boss.CurrentHealth, maxHp);
        // 차오르는 동안 잔상 바는 앞 바와 같이 간다(잔상이 먼저 가득 차 있으면 피해 흔적처럼 보인다).
        if (introFill.Active)
            delayed.ForceFill(shown01);

        if (counterGaugeFill != null)
        {
            bool hasGauge = boundGaugeBoss != null;
            if (counterGaugeFill.gameObject.activeSelf != hasGauge)
                counterGaugeFill.gameObject.SetActive(hasGauge);
            if (hasGauge)
                counterGaugeFill.fillAmount = boundGaugeBoss.CounterGauge01;   // 복제값 — 늦은 합류도 현재 값
        }

        // ④ 자폭 실드 — 복제 비율(SelfDestructShield01)을 HP 바 위 덮개로.
        float shield01 = boundGaugeBoss != null ? boundGaugeBoss.SelfDestructShield01 : 0f;
        if (shieldFill != null)
        {
            bool show = shield01 > 0f;
            if (shieldFill.gameObject.activeSelf != show) shieldFill.gameObject.SetActive(show);
            if (show) shieldFill.fillAmount = Mathf.Clamp01(shield01);
        }
    }

    private void OnDisable()
    {
        BindBoss(null);
    }

    private void BindBoss(Unit boss)
    {
        if (boundBoss != null)
            boundBoss.ClientHpChanged -= delayed.OnHpChanged;

        boundBoss = boss;
        boundGaugeBoss = boss as TwentyThreeBoss;

        if (boundBoss != null)
            boundBoss.ClientHpChanged += delayed.OnHpChanged;

        delayed.Bind(boundBoss != null ? boundBoss.CurrentHealth : 0);
    }

    private static Unit FindBoss()
    {
        var targets = BossHudTarget.Active;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null && targets[i].Unit != null && targets[i].Unit.CurrentHealth > 0)
                return targets[i].Unit;
        }

        return null;
    }
}
