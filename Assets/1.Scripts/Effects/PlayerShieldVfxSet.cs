using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 공용 보호막 연출 표(2026-11-02 민경). 공용 1개(<c>9.ScriptableObject/Effects/PlayerShieldVfxSet.asset</c>)를
/// 각 플레이어의 <see cref="PlayerShieldVfx"/> 가 읽는다.
///
/// <b>색은 받는 사람이 아니라 보호막 출처로 가른다.</b> 팔라딘 고유 보호막(수호의 잔광·불굴의 가호 — 본인·아군 모두) = 금색,
/// 그 외(거너 Q 등) = 흰색. 그래서 금색도 공용이다 — 불굴의 가호는 거너·어쌔신에게도 걸린다.
/// 금색으로 띄울 특성 보호막은 <see cref="holyTraitIds"/> 에 특성 ID 를 한 줄 추가하면 끝이다(코드 수정 없음).
/// </summary>
[CreateAssetMenu(fileName = "PlayerShieldVfxSet", menuName = "Effects/Player Shield Vfx Set")]
public sealed class PlayerShieldVfxSet : ScriptableObject
{
    [Header("금색 — 팔라딘 고유 보호막")]
    [Tooltip("보호막이 떠 있는 동안 도는 루프(FX_HolyShield_Entry).")]
    public EffectEntry holyLoop;
    [Tooltip("마지막 보호막이 피해로 소진될 때 한 번(FX_HolyShield_Break_Entry).")]
    public EffectEntry holyBreak;

    [Header("흰색 — 그 외(거너 Q 등)")]
    public EffectEntry allyLoop;
    public EffectEntry allyBreak;

    [Header("붙는 자리")]
    [Tooltip("위치는 캐릭터마다 StatusEffectVfxPresenter 의 자리 트랜스폼이 정한다.")]
    public StatusEffectVfxSet.Anchor anchor = StatusEffectVfxSet.Anchor.Body;
    [Tooltip("자리 기준 월드 단위 오프셋(m).")]
    public Vector3 offset;
    [Min(0.01f)] public float scale = 0.5f;

    [Header("금색 판정")]
    [Tooltip("금색으로 띄울 특성 보호막(ShieldType.Trait)의 특성 ID. ShieldType.HolyShield(옛 E 보호막)는 목록과 무관하게 금색.")]
    [SerializeField] private string[] holyTraitIds = { "PAL_3_E_SHIELD", "PAL_4_HEAL_SHIELD" };

    private HashSet<ulong> _holySources;

    /// <summary>이 보호막을 금색으로 띄우는가.</summary>
    public bool IsHoly(in ShieldInstance shield)
    {
        if (shield.type == ShieldType.HolyShield)
            return true;
        if (shield.type != ShieldType.Trait)
            return false;

        if (_holySources == null)
        {
            _holySources = new HashSet<ulong>();
            foreach (string id in holyTraitIds)
            {
                if (!string.IsNullOrEmpty(id))
                    _holySources.Add(TraitShieldRules.ChannelSource(TraitIdHash.Of(id)));
            }
        }
        return _holySources.Contains(shield.sourceId);
    }

    public EffectEntry Loop(bool holy) => holy ? holyLoop : allyLoop;
    public EffectEntry Break(bool holy) => holy ? holyBreak : allyBreak;

    // 인스펙터에서 목록을 고치면 캐시를 다시 만든다.
    private void OnValidate() => _holySources = null;
}
