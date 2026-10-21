using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 폭발 사망 연출. <see cref="CollapseDeath"/> 와 같은 부위 조각을 쓰되, 조각을 <b>위·바깥으로 튀겨</b>
/// 터지는 그림을 만들고 그 순간 폭발 이펙트를 한 번 낸다.
/// <see cref="IDeathEffect"/> 자리에 들어가는 형제라 <c>MonsterBase</c> 는 셋(디졸브·붕괴·폭발)을 구분하지 않는다.
///
/// <b>순서.</b> 사망 클립 끝 − <see cref="leadBeforeClipEnd"/> 에 터진다(타이밍은 <c>MonsterBase</c> 가 잰다)
/// → 조각이 <see cref="settleTime"/> 동안 굴러다님 → <see cref="exitDuration"/> 동안 녹아 사라짐.
/// 녹을 때 파티클은 내지 않는다(2026-10-21 민경 — 붕괴 때 쓰던 <c>FX_Dissolve_Death</c> 를 뺐다).
///
/// <b>네트워크.</b> <see cref="CollapseDeath"/> 와 같다 — <see cref="IDeathEffect.Play"/> 는 서버에서만 불리므로
/// Reliable RPC 로 전 피어에 퍼뜨리고 각자 로컬에서 터뜨린다. 조각이 튀는 모양은 피어마다 달라도 된다.
///
/// 🔴 <b>폭발 이펙트는 <see cref="EffectManager"/> 로 낸다.</b> 몬스터는 곧 디스폰되고 조각 루트도 녹기가 끝나면
/// Destroy 되므로, 어느 쪽의 자식으로 두든 도중에 잘린다. 풀링되는 별도 인스턴스라 끝까지 산다.
///
/// 🔴 <b>한 몹에 <see cref="CollapseDeath"/> 와 같이 두지 말 것</b> — <c>MonsterBase</c> 는
/// <c>GetComponent&lt;IDeathEffect&gt;()</c> 로 하나만 집는다. 붕괴 설정 도구는 이 컴포넌트가 있으면 건너뛴다.
/// </summary>
[DisallowMultipleComponent]
public class ExplodeDeath : NetworkBehaviour, IDeathEffect
{
    [Tooltip("이 몬스터의 부위 조각 에셋(붕괴와 같은 것). Effects/Monster Part Set 으로 만들고 인스펙터에서 Bake 한다")]
    [SerializeField] MonsterPartSet partSet;

    [Header("폭발")]
    [Tooltip("폭발 이펙트. 비우면 카탈로그 Mob_Death_Explosion(잡몹 기본).\n" +
             "엘리트처럼 다른 폭발을 쓸 몹만 채운다 — 프리워밍되게 카탈로그에도 같이 올려 둘 것(Mob_Death_Explosion_Elite)")]
    [SerializeField] EffectEntry explosionEffect;

    [Tooltip("폭발 이펙트를 낼 높이(루트 기준 위로, m).\n0 = 터지는 순간 몸 렌더러의 경계 중심(사망 자세 그대로)")]
    [SerializeField, Min(0f)] float burstHeight = 0f;

    [Tooltip("폭발 이펙트 배율(카탈로그 Mob_Death_Explosion)")]
    [SerializeField, Min(0.01f)] float explosionScale = 1f;

    [Tooltip("조각이 바깥으로 받는 속도 범위(m/s, 수평)")]
    [SerializeField] Vector2 outwardSpeed = new Vector2(1.5f, 2.5f);

    [Tooltip("조각이 위로 받는 속도 범위(m/s)")]
    [SerializeField] Vector2 upwardSpeed = new Vector2(2f, 3f);

    [Tooltip("조각의 초기 회전 속도 범위(도/초)")]
    [SerializeField] Vector2 spinRange = new Vector2(180f, 360f);

    [Header("사라지기")]
    [Tooltip("조각이 사라지는 방식.\n" +
             "Dissolve = 노이즈로 녹는다(기존 사망 연출과 같은 그림)\n" +
             "Fade = 원본 머티리얼 그대로 투명해진다 — 셀 음영·아웃라인 유지\n" +
             "None = 그냥 없어진다")]
    [SerializeField] MonsterCollapseDebris.ExitMode exitMode = MonsterCollapseDebris.ExitMode.Dissolve;

    [Tooltip("VFX/Stylized Surface Dissolve 셰이더. FlatKit 룩을 유지한 채 녹인다")]
    [SerializeField] Shader stylizedDissolveShader;

    [Tooltip("녹는 모양을 정하는 노이즈. 비우면 한 번에 사라진다 — M_Dissolve_Template 의 것과 같은 걸 쓰면 된다")]
    [SerializeField] Texture dissolveNoise;

    [Tooltip("노이즈 타일링. 크면 잘게, 작으면 뭉텅이로 녹는다")]
    [SerializeField, Min(0.01f)] float dissolveNoiseTiling = 4f;

    [Tooltip("FlatKit 이 아닌 머티리얼의 폴백 템플릿(M_Dissolve_Template)")]
    [SerializeField] Material dissolveTemplate;

    [Header("타이밍")]
    [Tooltip("조각이 물리로 굴러다니는 시간(초). 지나면 녹기 시작한다")]
    [SerializeField, Min(0f)] float settleTime = 1.2f;

    [Tooltip("조각이 사라지는 데 걸리는 시간(초)")]
    [SerializeField, Min(0f)] float exitDuration = 0.6f;

    [Tooltip("디스폰까지의 여유(초). 조각은 떼어 놓여 있어 디스폰과 무관하게 사니 짧아도 된다.\n" +
             "🔴 0 으로 두지 말 것 — 다른 피어가 RPC 를 받기 전에 오브젝트가 사라지면 아무도 못 본다")]
    [SerializeField, Min(0f)] float despawnGrace = 0.3f;

    [Tooltip("사망 클립이 끝나기 이 시간(초) 전에 터진다. 0 = 클립 끝에 딱 맞춰")]
    [SerializeField, Min(0f)] float leadBeforeClipEnd = 0.1f;

    [Tooltip("사망 클립이 끝난 뒤 이 시간(초)을 더 기다렸다가 터진다. 0 = 바로")]
    [SerializeField, Min(0f)] float delayAfterClipEnd = 0f;

    public float LeadBeforeClipEnd => leadBeforeClipEnd;
    public float DelayAfterClipEnd => delayAfterClipEnd;

    bool _played;

    #region IDeathEffect

    /// <summary>[서버] 연출을 전 피어에 요청하고, 잠시 뒤 <paramref name="onComplete"/>(= 디스폰)를 부른다.</summary>
    public void Play(Action onComplete)
    {
        if (IsSpawned)
            PlayExplodeRpc();
        else
            PlayLocal();   // 네트워크가 없는 테스트 씬 폴백

        if (onComplete != null)
            StartCoroutine(CompleteAfter(onComplete));
    }

    IEnumerator CompleteAfter(Action onComplete)
    {
        yield return new WaitForSeconds(despawnGrace);
        onComplete.Invoke();
    }

    #endregion

    // CollapseDeath 와 같은 이유로 reliable 이다 — 사망 때 딱 한 번 오는 신호다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
    void PlayExplodeRpc() => PlayLocal();

    void PlayLocal()
    {
        if (_played) return;   // 재전송·재진입 방어
        _played = true;

        // 원본을 감추기 '전에' 위치를 잰다 — 꺼진 렌더러의 경계는 믿을 수 없다.
        Vector3 burst = BurstPoint();
        PlayEffect(e => explosionEffect != null ? explosionEffect : e.Catalog.Mob_Death_Explosion, burst, explosionScale);

        if (partSet == null || !partSet.IsBaked)
        {
            Edit.LogWarning($"[Explode] {name}: MonsterPartSet 이 비었거나 Bake 되지 않았습니다. " +
                            "조각 없이 사라집니다.", this);
            return;
        }

        MonsterCollapseDebris.Spawn(partSet, transform, new MonsterCollapseDebris.Config
        {
            scatterSpeedRange = outwardSpeed,
            upwardSpeedRange = upwardSpeed,
            spinRange = spinRange,
            settleTime = settleTime,
            exitDuration = exitDuration,
            exitMode = exitMode,
            dissolveTemplate = dissolveTemplate,
            stylizedDissolve = stylizedDissolveShader,
            dissolveNoise = dissolveNoise,
            dissolveNoiseTiling = dissolveNoiseTiling,
        });

        HideOriginal();
    }

    /// <summary><see cref="burstHeight"/> 가 0 이면 몸 렌더러의 경계 중심, 아니면 루트 + 위로 그 높이.</summary>
    Vector3 BurstPoint()
    {
        if (burstHeight > 0f) return transform.position + Vector3.up * burstHeight;

        bool any = false;
        Bounds b = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled) continue;
            if (any) b.Encapsulate(r.bounds);
            else { b = r.bounds; any = true; }
        }
        return any ? b.center : transform.position + Vector3.up;
    }

    static void PlayEffect(Func<EffectManager, EffectEntry> pick, Vector3 at, float scale)
    {
        if (!EffectManager.TryGet(out EffectManager effects)) return;

        EffectEntry entry = pick(effects);
        if (entry == null)
        {
            Edit.LogWarning("[Explode] 폭발 이펙트가 비었습니다(explosionEffect · 카탈로그 Mob_Death_Explosion).");
            return;
        }

        effects.Play(entry, at, Quaternion.identity, scale);
    }

    /// <summary>
    /// 원본을 감춘다. 조각이 같은 자리에 겹쳐 있으므로 <b>같은 프레임에</b> 꺼야 두 겹으로 안 보인다.
    /// 인터럽트 오버레이(<see cref="DissolveOverlay"/>)도 같이 걷는다(<see cref="CollapseDeath"/> 와 같은 이유).
    /// </summary>
    void HideOriginal()
    {
        foreach (DissolveOverlay overlay in GetComponentsInChildren<DissolveOverlay>(true))
            overlay.Hide();

        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            // 파티클·트레일은 끄지 않는다 — 사망 연출 자신이 거기 섞여 있을 수 있다.
            if (all[i] is MeshRenderer || all[i] is SkinnedMeshRenderer)
                all[i].enabled = false;
        }
    }
}
