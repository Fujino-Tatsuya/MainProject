using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 나누는 불굴(PAL_2_SHARE_HEAL) 전이 연출(2026-11-02 민경 grill — PLAN.md 「나누는 불굴 전이 VFX」).
/// <c>Player_Paladin</c> Variant 의 <c>VFX</c> 에 붙는다.
///
/// 서버가 초과 회복을 아군에게 넘기면 특성 연출 공용 경로로 <see cref="TraitFxTrait.PaladinShareHeal"/> Hit 사건이 전 피어에 난다
/// (<c>PlayerTraitEffects.ShareOverflow</c> → <c>Player.TraitSkillFxRpc</c> → <see cref="TraitSkillFx.Raised"/>).
/// 그 사건 중 <b>시전자가 이 팔라딘인 것만</b> 받아:
/// ① 팔라딘 몸에 출발 소용돌이(따라감) ② 리본(<see cref="HealTransferRibbon"/>)을 아군에게 날림 →
/// ③ 리본이 닿는 순간 아군의 공용 힐 원샷(<see cref="PlayerOneShotVfx.Heal"/>).
///
/// 회복 수치는 서버에서 이미 들어갔다 — 여기는 연출만 늦게 닿는다. 아군이 날아가는 중 쓰러지거나
/// 끊기거나 연출 잠금으로 숨으면 리본은 흐려지고 힐 원샷은 생략한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PaladinShareHealVfx : MonoBehaviour
{
    [Tooltip("출발 소용돌이 — 팔라딘 몸을 엔트리 길이만큼 따라간다. 비우면 생략")]
    [SerializeField] EffectEntry sourceSwirl;
    [Tooltip("출발 소용돌이를 붙일 자리 기준 오프셋(월드, m)")]
    [SerializeField] Vector3 sourceSwirlOffset = Vector3.zero;
    [SerializeField, Min(0.01f)] float sourceSwirlScale = 1f;

    [Tooltip("전이 리본 엔트리 — 프리팹 루트에 HealTransferRibbon. 도착 시간은 그 컴포넌트의 travelSeconds")]
    [SerializeField] EffectEntry ribbon;

    static readonly List<GameObject> InstanceBuffer = new List<GameObject>();

    Player _player;

    void Awake() => _player = GetComponentInParent<Player>();

    void OnEnable() => TraitSkillFx.Raised += HandleTraitFx;

    void OnDisable() => TraitSkillFx.Raised -= HandleTraitFx;

    void HandleTraitFx(TraitFxEvent e)
    {
        if (e.Trait != TraitFxTrait.PaladinShareHeal || e.Kind != TraitFxKind.Hit) return;
        if (_player == null || e.Caster != _player || e.Target == null) return;
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        PlaySourceSwirl(effects);
        LaunchRibbon(effects, e.Target);
    }

    void PlaySourceSwirl(EffectManager effects)
    {
        if (sourceSwirl == null) return;

        EffectHandle handle = effects.PlayLooping(sourceSwirl, _player.transform, sourceSwirlOffset, sourceSwirlScale);
        effects.StartCoroutine(ReleaseAfter(handle, sourceSwirl.ResolvedDuration));
    }

    void LaunchRibbon(EffectManager effects, Unit target)
    {
        Transform ally = target.transform;
        if (ribbon == null)
        {
            // 리본이 없어도 아군 힐은 낸다(도착 지연 없이).
            PlayAllyHeal(target);
            return;
        }

        EffectHandle handle = effects.PlayRecallable(ribbon, _player.transform.position, Quaternion.identity);
        effects.GetInstances(handle, InstanceBuffer);
        bool launched = false;
        foreach (GameObject instance in InstanceBuffer)
        {
            HealTransferRibbon strands = instance.GetComponentInChildren<HealTransferRibbon>(true);
            if (strands == null) continue;
            strands.Begin(_player.transform, ally, () => IsReceivable(target), () => PlayAllyHeal(target));
            launched = true;
            break;
        }
        InstanceBuffer.Clear();

        if (!launched)
        {
            Edit.LogWarning($"[PaladinShareHealVfx] 리본 엔트리 '{ribbon.name}' 에 HealTransferRibbon 이 없다 — 아군 힐만 바로 낸다.", this);
            PlayAllyHeal(target);
        }
    }

    // 쓰러짐·연출 숨김이면 도착 연출을 생략한다(서버 전달 조건과 같은 기준).
    static bool IsReceivable(Unit target)
    {
        if (target == null || !target.isActiveAndEnabled) return false;
        if (target.TryGetComponent(out PlayerLifeCycleController life) && life.State != PlayerLifeState.Alive) return false;
        return !(target.TryGetComponent(out PlayerEncounterLock encounterLock) && encounterLock.IsCinematicHidden);
    }

    static void PlayAllyHeal(Unit target)
    {
        PlayerOneShotVfxPresenter oneShots = PlayerOneShotVfxPresenter.For(target);
        if (oneShots != null) oneShots.Play(PlayerOneShotVfx.Heal);
    }

    static IEnumerator ReleaseAfter(EffectHandle handle, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (EffectManager.Instance != null) EffectManager.Instance.Release(handle);
    }
}
