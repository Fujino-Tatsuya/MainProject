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
    [SerializeField] private DelayedHealthBar delayed = new DelayedHealthBar();

    private Unit boundBoss;

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
