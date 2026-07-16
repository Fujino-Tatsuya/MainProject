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

    private void Update()
    {
        Unit boss = FindBoss();

        bool shouldShow = boss != null && boss.CurrentHealth > 0;
        if (barRoot != null && barRoot.activeSelf != shouldShow)
            barRoot.SetActive(shouldShow);

        if (!shouldShow || hpFill == null)
            return;

        int maxHp = boss.FinalMaxHp;
        hpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)boss.CurrentHealth / maxHp) : 0f;
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
