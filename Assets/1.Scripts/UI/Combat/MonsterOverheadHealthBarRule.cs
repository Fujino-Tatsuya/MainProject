using UnityEngine;

/// <summary>
/// 일반 몬스터·중간 보스 발밑 체력바 규칙(모든 피어, 표시만 로컬 판단).
/// 프리팹에 붙이지 않는다 — <see cref="MonsterBase"/> 스폰 때 <see cref="TryInstall"/> 가 런타임에 이 규칙을 붙이고
/// 등급별 바 프리팹(<see cref="MonsterOverheadHealthBarSettings.BarPrefab"/>)을 자식으로 만들어 발밑에 놓는다.
/// 표시 판단은 <see cref="MonsterOverheadHealthBarVisibility"/>(피격 → 표시, 전투 종료 + N초 → 숨김, 사망 → 즉시 숨김).
/// 몬스터에 자체 <see cref="IOverheadHealthBarRule"/> 이 있으면(튜토리얼 스피너봇) 바만 붙이고 표시·색·라벨은 그 규칙을 따른다.
/// 상세: Docs/tech/handoff-monster-healthbar.md
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterOverheadHealthBarRule : MonoBehaviour, IOverheadHealthBarRule
{
    static bool _warnedMissingSettings;

    readonly MonsterOverheadHealthBarVisibility _visibility = new MonsterOverheadHealthBarVisibility();
    MonsterBase _monster;
    MonsterOverheadHealthBarSettings _settings;
    UnitOverheadHealthBar _bar;
    bool _shown;

    /// <summary>
    /// 몬스터에 체력바를 붙인다(멱등). 판단은 <see cref="MonsterOverheadHealthBarVisibility.DecideInstall"/>.
    /// 이미 붙어 있으면 표시 상태만 처음으로 되돌린다(풀 재사용).
    /// </summary>
    public static void TryInstall(MonsterBase monster)
    {
        if (monster == null)
            return;

        monster.TryGetComponent(out MonsterOverheadHealthBarRule existing);
        var decision = MonsterOverheadHealthBarVisibility.DecideInstall(
            monster.Rank,
            monster.GetComponent<BossHudTarget>() != null,
            existing != null,
            monster.GetComponentInChildren<UnitOverheadHealthBar>(true) != null,
            monster.GetComponent<IOverheadHealthBarRule>() != null);

        if (decision == MonsterHealthBarInstall.ResetExisting)
        {
            existing._visibility.Reset();
            return;
        }
        if (decision == MonsterHealthBarInstall.Skip)
            return;

        var settings = Resources.Load<MonsterOverheadHealthBarSettings>(MonsterOverheadHealthBarSettings.ResourcePath);
        UnitOverheadHealthBar prefab = settings != null ? settings.BarPrefab(monster.Rank) : null;
        if (prefab == null)
        {
            if (!_warnedMissingSettings)
            {
                _warnedMissingSettings = true;
                Debug.LogWarning($"[MonsterHealthBar] Resources/{MonsterOverheadHealthBarSettings.ResourcePath} 가 없거나 {monster.Rank} 바 프리팹이 비어 몬스터 체력바를 붙이지 않는다.");
            }
            return;
        }

        // 규칙을 먼저 붙인다 — 바의 Awake 가 부모에서 IOverheadHealthBarRule 을 찾는다.
        MonsterOverheadHealthBarRule rule = null;
        if (decision == MonsterHealthBarInstall.AttachWithRule)
        {
            rule = monster.gameObject.AddComponent<MonsterOverheadHealthBarRule>();
            rule._monster = monster;
            rule._settings = settings;
        }

        UnitOverheadHealthBar bar = Instantiate(prefab, monster.transform);
        bar.transform.localPosition = Vector3.zero;
        bar.transform.localRotation = Quaternion.identity;
        bar.ConfigurePlacement(0f, MonsterOverheadHealthBarVisibility.FootScreenOffset(settings.FootScreenOffset(monster.Rank)));
        if (rule != null)
            rule._bar = bar;
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
    public Color FillColor => _bar != null ? _bar.DefaultFillColor : Color.red;
    public string Label => null;
}
