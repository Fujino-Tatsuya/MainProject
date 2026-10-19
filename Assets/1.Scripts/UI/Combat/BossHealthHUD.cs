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

        bool shouldShow = boss != null && boss.CurrentHealth > 0;
        if (barRoot != null && barRoot.activeSelf != shouldShow)
            barRoot.SetActive(shouldShow);

        if (!shouldShow)
            return;

        int maxHp = boss.FinalMaxHp;
        if (hpFill != null)
            hpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)boss.CurrentHealth / maxHp) : 0f;

        delayed.Tick(Time.deltaTime, boss.CurrentHealth, maxHp);

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
