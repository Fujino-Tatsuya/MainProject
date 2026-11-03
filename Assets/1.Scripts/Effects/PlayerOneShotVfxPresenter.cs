using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어 공용 원샷 이펙트 재생기(2026-11-02 민경). 각 플레이어 Variant 의 <c>VFX/States</c> 에
/// <see cref="StatusEffectVfxPresenter"/> 와 나란히 붙는다 — 붙는 자리는 그 컴포넌트의 자리를 그대로 읽는다.
///
/// <b>재생만 한다.</b> 언제 부를지·누구 화면에서 부를지는 부르는 쪽(계기)이 정한다.
/// 반드시 <b>전 피어 경로</b>(RPC 수신부·복제 상태 감시)에서 부를 것 — 여기는 네트워크를 모른다.
///
/// <b>따라가기.</b> 원샷이지만 <see cref="EffectManager.PlayLooping"/> 로 자리를 추종시킨다
/// (일반 원샷 <c>Play</c> 는 위치 고정이라 재생 중 걸어가면 뒤에 남는다). 해제 시점은 엔트리가 정한다:
/// outroDuration 이 있으면 곧바로 해제해 그 동안 따라가며 흩어지고(레벨업 — 옛 TraitAcquiredVfxPresenter 와 같은 길이),
/// 없으면 엔트리 길이(<see cref="EffectEntry.ResolvedDuration"/>)만큼 따라간 뒤 해제한다(힐).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerOneShotVfxPresenter : MonoBehaviour
{
    [Tooltip("종류 → 이펙트 표(플레이어 공용 PlayerOneShotVfxSet).")]
    [SerializeField] private PlayerOneShotVfxSet set;

    [Tooltip("붙는 자리를 빌려 올 상태이상 Presenter. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private StatusEffectVfxPresenter anchors;

    private Transform unitRoot;

    private void Awake()
    {
        if (anchors == null)
            anchors = GetComponent<StatusEffectVfxPresenter>();
        Unit unit = GetComponentInParent<Unit>();
        unitRoot = unit != null ? unit.transform : transform.root;
    }

    /// <summary>그 유닛(플레이어)의 재생기. 플레이어가 아니거나 없으면 null.</summary>
    public static PlayerOneShotVfxPresenter For(Component unit) =>
        unit != null ? unit.GetComponentInChildren<PlayerOneShotVfxPresenter>(true) : null;

    /// <summary>[이 피어] 원샷 하나를 재생한다. 표에 줄이 없으면 조용히 넘어간다.</summary>
    public void Play(PlayerOneShotVfx kind)
    {
        PlayerOneShotVfxSet.Row row = set != null ? set.Find(kind) : null;
        if (row == null || !EffectManager.TryGet(out EffectManager effects, this))
            return;

        Transform anchor = anchors != null ? anchors.GetAnchor(row.anchor) : null;
        if (anchor == null)
            anchor = unitRoot;

        if (!row.follow)
        {
            effects.Play(row.effect, anchor.position + row.offset, anchor.rotation, row.scale);
            return;
        }

        EffectHandle handle = effects.PlayLooping(row.effect, anchor, row.offset, row.scale);
        if (row.effect.ResolvedOutroDuration > 0f)
        {
            effects.Release(handle);
            return;
        }

        // 이 오브젝트가 꺼져도(쓰러짐·디스폰) 해제는 나가야 한다 — 매니저 쪽 코루틴에 태운다.
        effects.StartCoroutine(ReleaseAfter(handle, row.effect.ResolvedDuration));
    }

    private static IEnumerator ReleaseAfter(EffectHandle handle, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        if (EffectManager.Instance != null)
            EffectManager.Instance.Release(handle);
    }
}
