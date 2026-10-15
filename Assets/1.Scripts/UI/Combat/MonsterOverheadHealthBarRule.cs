using UnityEngine;

/// <summary>
/// 일반 몬스터·중간 보스 머리 위 체력바 규칙(모든 피어, 표시만 로컬 판단).
/// 프리팹에 붙이지 않는다 — <see cref="MonsterBase"/> 스폰 때 <see cref="TryInstall"/> 가 런타임에 이 규칙을 붙이고
/// 공용 바 프리팹(<see cref="MonsterOverheadHealthBarSettings.BarPrefab"/>)을 자식으로 만든다.
/// 표시 판단은 <see cref="MonsterOverheadHealthBarVisibility"/>(피격 → 표시, 전투 종료 + N초 → 숨김, 사망 → 즉시 숨김).
/// 상세: Docs/tech/handoff-monster-healthbar.md
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterOverheadHealthBarRule : MonoBehaviour, IOverheadHealthBarRule
{
    static bool _warnedMissingSettings;

    readonly MonsterOverheadHealthBarVisibility _visibility = new MonsterOverheadHealthBarVisibility();
    MonsterBase _monster;
    MonsterOverheadHealthBarSettings _settings;
    bool _shown;

    /// <summary>
    /// 몬스터에 체력바를 붙인다(멱등). 최종 보스(Rank=Boss·BossHudTarget)와 이미 머리 위 바가 있는 프리팹은 건너뛴다.
    /// 이미 붙어 있으면 표시 상태만 처음으로 되돌린다(풀 재사용).
    /// </summary>
    public static void TryInstall(MonsterBase monster)
    {
        if (monster == null)
            return;

        if (monster.TryGetComponent(out MonsterOverheadHealthBarRule existing))
        {
            existing._visibility.Reset();
            return;
        }

        bool hasBossHud = monster.GetComponent<BossHudTarget>() != null;
        bool hasBar = monster.GetComponentInChildren<UnitOverheadHealthBar>(true) != null;
        if (!MonsterOverheadHealthBarVisibility.ShouldAttach(monster.Rank, hasBossHud, hasBar))
            return;

        var settings = Resources.Load<MonsterOverheadHealthBarSettings>(MonsterOverheadHealthBarSettings.ResourcePath);
        if (settings == null || settings.BarPrefab == null)
        {
            if (!_warnedMissingSettings)
            {
                _warnedMissingSettings = true;
                Debug.LogWarning($"[MonsterHealthBar] Resources/{MonsterOverheadHealthBarSettings.ResourcePath} 가 없거나 barPrefab 이 비어 몬스터 체력바를 붙이지 않는다.");
            }
            return;
        }

        // 규칙을 먼저 붙인다 — 바의 Awake 가 부모에서 IOverheadHealthBarRule 을 찾는다.
        var rule = monster.gameObject.AddComponent<MonsterOverheadHealthBarRule>();
        rule._monster = monster;
        rule._settings = settings;

        UnitOverheadHealthBar bar = Instantiate(settings.BarPrefab, monster.transform);
        bar.transform.localPosition = Vector3.zero;
        bar.transform.localRotation = Quaternion.identity;
        bar.ConfigurePlacement(
            settings.WorldHeightOffset(monster.Rank),
            MonsterOverheadHealthBarVisibility.HeightScale(monster.Rank, settings.MidBossHeightScale));
    }

    void Update()
    {
        if (_monster == null || _settings == null)
        {
            _shown = false;
            return;
        }

        _shown = _visibility.Tick(Time.time, _monster.CurrentHealth, _monster.State, _settings.HideDelaySeconds);
    }

    public bool ShouldShow => _shown;
    public Color FillColor => _settings != null ? _settings.FillColor : Color.red;
    public string Label => null;
}
