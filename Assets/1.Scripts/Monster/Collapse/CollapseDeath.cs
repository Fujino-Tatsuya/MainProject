using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 붕괴 사망 연출. 캐릭터를 통째로 녹이는 대신 <b>부위 덩어리로 해체해 무너뜨린다</b>.
/// <see cref="DissolveDeath"/> 와 같은 자리(<see cref="IDeathEffect"/>)에 들어가는 형제다 —
/// <c>MonsterBase</c> 는 둘을 구분하지 않는다.
///
/// <b>어떻게 원본 모양이 나오나.</b> <see cref="MonsterPartSet"/> 이 스킨 메시를 본 단위로
/// 미리 갈라 두고, 조각 정점을 <b>본 로컬 공간</b>에 담아 둔다. 죽는 순간 각 조각을 제 본의
/// 월드 트랜스폼에 놓으면 <b>사망 애니메이션이 끝난 그 자세</b>가 근사 없이 재현된다.
/// 미리 잘라 둔 스태틱 파츠로는 안 되는 부분이다 — 그쪽은 바인드 포즈를 들고 있다.
///
/// <b>왜 NetworkBehaviour 인가.</b> <see cref="IDeathEffect.Play"/> 는 서버에서만 불린다
/// (<c>MonsterBase.EnterDead</c> 가 <c>if (!IsServer) return;</c> 뒤에 있다). 여기서 바로
/// 재생하면 <b>호스트에서만 보인다</b> — 이 레포가 반복해서 낸 버그다. RPC 로 전 피어에 퍼뜨리고
/// 각 피어가 자기 로컬에서 무너뜨린다. 좌표는 싣지 않는다(조각이 흩어지는 모양은 연출이라
/// 피어마다 달라도 되고, 그래서 조각이 게임플레이에 닿으면 안 된다).
///
/// 🔴 <b>조각은 몬스터의 자식이 아니다.</b> 몬스터는 곧 디스폰되므로 자식으로 두면 조각이
/// 한 프레임 만에 증발한다. <see cref="MonsterCollapseDebris"/> 가 떼어 놓인 루트로 살고
/// 제 수명을 제가 센다 — 그래서 여기서는 <see cref="despawnGrace"/> 를 짧게 잡아도 된다.
/// </summary>
[DisallowMultipleComponent]
public class CollapseDeath : NetworkBehaviour, IDeathEffect
{
    [Tooltip("이 몬스터의 부위 조각 에셋. Effects/Monster Part Set 으로 만들고 인스펙터에서 Bake 한다")]
    [SerializeField] MonsterPartSet partSet;

    // 🔴 **기본은 Dissolve 다** — 기존 사망 연출과 같은 그림(노이즈 침식 + 시안 가장자리).
    //    디졸브 셰이더는 URP Lit 라 FlatKit 의 셀 음영·아웃라인은 녹는 동안 빠진다.
    //    그 질감까지 지키고 싶으면 Fade 를 고르면 된다(원본 머티리얼을 투명으로만 바꾼다).
    [Tooltip("조각이 사라지는 방식.\n" +
             "Dissolve = 노이즈로 녹는다(기존 사망 연출과 같은 그림)\n" +
             "Fade = 원본 머티리얼 그대로 투명해진다 — 셀 음영·아웃라인 유지\n" +
             "None = 그냥 없어진다")]
    [SerializeField] MonsterCollapseDebris.ExitMode exitMode = MonsterCollapseDebris.ExitMode.Dissolve;

    // 🔴 **이게 있어야 셀 음영이 유지된 채로 녹는다.** FlatKit StylizedSurface 의 사본에
    //    깎기만 더한 셰이더라, 원본 머티리얼을 복제한 뒤 셰이더만 바꾸면 설정이 전부 살아남는다.
    //    비우면 아래 템플릿으로 통째로 갈아끼우는 옛 경로로 떨어진다(질감이 바뀐다).
    [Tooltip("VFX/Stylized Surface Dissolve 셰이더. FlatKit 룩을 유지한 채 녹인다")]
    [SerializeField] Shader stylizedDissolveShader;

    [Tooltip("녹는 모양을 정하는 노이즈. 비우면 한 번에 사라진다 — M_Dissolve_Template 의 것과 같은 걸 쓰면 된다")]
    [SerializeField] Texture dissolveNoise;

    [Tooltip("노이즈 타일링. 크면 잘게, 작으면 뭉텅이로 녹는다")]
    [SerializeField, Min(0.01f)] float dissolveNoiseTiling = 4f;

    [Tooltip("FlatKit 이 아닌 머티리얼의 폴백 템플릿(DissolveDeath 와 같은 M_Dissolve_Template)")]
    [SerializeField] Material dissolveTemplate;

    [Tooltip("무너지는 순간 터뜨릴 파티클(먼지·불꽃). 비워도 된다.\n" +
             "조각 루트의 자식으로 생기므로 몬스터가 디스폰돼도 끝까지 재생된다")]
    [SerializeField] ParticleSystem particlePrefab;

    [Header("무너지는 모양")]
    [Tooltip("조각이 받는 초기 속도(m/s). 0 이면 순수 낙하.\n" +
             "🔴 폭발이 아니다 — 크게 주면 무너지는 게 아니라 터지는 그림이 된다")]
    [SerializeField, Min(0f)] float scatterSpeed = 0.5f;

    [Tooltip("조각의 초기 회전 속도 범위(도/초)")]
    [SerializeField] Vector2 spinRange = new Vector2(40f, 160f);

    [Header("타이밍")]
    [Tooltip("조각이 물리로 굴러다니는 시간(초). 지나면 녹기 시작한다")]
    [SerializeField, Min(0f)] float settleTime = 1.2f;

    [Tooltip("조각이 사라지는 데 걸리는 시간(초)")]
    [UnityEngine.Serialization.FormerlySerializedAs("dissolveDuration")]
    [SerializeField, Min(0f)] float exitDuration = 0.6f;

    [Tooltip("디스폰까지의 여유(초). 조각은 떼어 놓여 있어 디스폰과 무관하게 사니 짧아도 된다.\n" +
             "🔴 0 으로 두지 말 것 — 다른 피어가 RPC 를 받기 전에 오브젝트가 사라지면 아무도 못 본다")]
    [SerializeField, Min(0f)] float despawnGrace = 0.3f;

    [Tooltip("사망 클립이 끝나기 이 시간(초) 전에 붕괴를 시작한다. 0 = 클립 끝에 딱 맞춰")]
    [SerializeField, Min(0f)] float leadBeforeClipEnd = 0.1f;

    [Tooltip("사망 클립이 끝난 뒤 이 시간(초)을 더 기다렸다가 시작한다. 0 = 바로")]
    [SerializeField, Min(0f)] float delayAfterClipEnd = 0f;

    public float LeadBeforeClipEnd => leadBeforeClipEnd;
    public float DelayAfterClipEnd => delayAfterClipEnd;

    bool _played;

    #region IDeathEffect

    /// <summary>[서버] 연출을 전 피어에 요청하고, 잠시 뒤 <paramref name="onComplete"/>(= 디스폰)를 부른다.</summary>
    public void Play(Action onComplete)
    {
        if (IsSpawned)
            PlayCollapseRpc();
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

    // DissolveDeath 와 같은 이유로 reliable 이다 — 사망 때 딱 한 번 오는 신호라서,
    // 유실되면 조각 없이 툭 사라진다. 한 번뿐이라 비용 차이도 없다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
    void PlayCollapseRpc() => PlayLocal();

    void PlayLocal()
    {
        if (_played) return;   // 재전송·재진입 방어
        _played = true;

        if (partSet == null || !partSet.IsBaked)
        {
            Edit.LogWarning($"[Collapse] {name}: MonsterPartSet 이 비었거나 Bake 되지 않았습니다. " +
                            "연출 없이 사라집니다.", this);
            return;
        }

        // 🔴 조각을 세우기 '전에' 본을 읽어야 한다. 아래에서 렌더러를 꺼도 본 트랜스폼은 그대로지만,
        //    순서를 지켜 두면 나중에 애니메이터를 끄는 변경이 들어와도 안전하다.
        var debris = MonsterCollapseDebris.Spawn(partSet, transform, new MonsterCollapseDebris.Config
        {
            scatterSpeed = scatterSpeed,
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

        if (particlePrefab != null)
        {
            // 조각 루트에 붙인다 — 몬스터에 붙이면 디스폰과 함께 잘린다.
            Transform parent = debris != null ? debris.transform : null;
            ParticleSystem ps = Instantiate(particlePrefab, transform.position, transform.rotation, parent);
            ps.Play(true);
            if (parent == null) Destroy(ps.gameObject, 4f);
        }
    }

    /// <summary>
    /// 원본을 감춘다. 조각이 같은 자리에 겹쳐 있으므로 <b>같은 프레임에</b> 꺼야 두 겹으로 안 보인다.
    ///
    /// 🔴 인터럽트 오버레이(<see cref="DissolveOverlay"/>)도 같이 걷는다. 같은 메시를 한 번 더
    /// 그리는 겹이라, 본체만 끄면 그 껍데기가 공중에 남는다.
    /// </summary>
    void HideOriginal()
    {
        // 오버레이는 렌더러와 같은 오브젝트에 붙는다(DissolveOverlay 의 RequireComponent).
        // 몹마다 어느 렌더러인지 달라서 전부 훑는다 — Gauntlet·Spinner·Wall 이 여기 걸린다.
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
