using UnityEngine;

/// <summary>
/// 어쌔신 특성 절제된 분노(ASN_2_RESTRAINT) 연출 — 봉인 순간 + 단검 봉인 띠(2026-11-04 민경, PLAN.md 3차 결정 · handoff-restraint-vfx.md).
/// <c>Player_Assassin/VFX/Restraint</c> 에 붙는다. 판정·수치 무관, 새 네트워크 없음 — 전 피어가 같은 값(<see cref="AssassinState.IsRestrained"/> =
/// 특성 획득 행 복제)을 보고 각자 낸다.
///
/// - <b>봉인</b>: <c>IsRestrained</c> 가 false → true 로 바뀌는 걸 본 순간(변신 중이면 변신이 끝나는 순간) 플레이어 발밑에
///   <c>EffectCatalog.Assassin_Restraint_Seal</c>(사슬 3줄이 조여들고 오라가 터지는 원샷) + R 소용돌이(<c>Assassin_R_BladeVortex</c>) 루프를 같이 켜고,
///   사슬이 다 조이는 <see cref="sealLockTime"/> 에 소용돌이를 해제(엔트리 outro = _End — R 이 갇혀 꺼짐).
/// - <b>단검 띠</b>: 양손 단검 소켓(<c>Dagger_Weapon_R/L/VFX_EnhanceAura</c>)의 <b>자식</b>으로 띠 프리팹(MeshRenderer 사슬)을 만든다 — 단검 배율을 그대로 받는다.
///   봉인 오라가 터지는 <see cref="bandShowDelay"/> 부터 알파 페이드 인, 특성이 풀리면 페이드 아웃. 재질 <c>_BaseColor</c> 알파를 블록으로 바꾼다.
/// - 처음 본 값(늦은 합류·재접속·이 오브젝트 재활성)은 봉인 없이 띠만 바로(페이드 없이) 맞춘다. 사망·다운은 그대로(특성 유지).
/// 비어 있는 칸은 해당 연출만 빠진다.
/// </summary>
public sealed class AssassinRestraintView : MonoBehaviour
{
    [Header("단검 띠")]
    [Tooltip("단검 소켓 — [0] 오른손 Dagger_Weapon_R/VFX_EnhanceAura, [1] 왼손. 띠가 이 자식으로 붙는다.")]
    [SerializeField] private Transform[] daggerSockets = new Transform[2];

    [Tooltip("띠 프리팹 — [0] 오른손 FX_Restraint_DaggerBand_R, [1] 왼손 _L(소켓 축이 달라 손마다 메시가 따로).")]
    [SerializeField] private GameObject[] bandPrefabs = new GameObject[2];

    [Tooltip("띠가 나타나는 페이드 길이(초).")]
    [SerializeField, Min(0f)] private float bandFadeIn = 0.4f;

    [Tooltip("띠가 사라지는 페이드 길이(초).")]
    [SerializeField, Min(0f)] private float bandFadeOut = 0.4f;

    [Header("봉인 순간")]
    [Tooltip("봉인 시작 → 띠 페이드 인 시작(초). 기본 = 봉인 오라가 터지는 순간(빌더 SealBurstTime).")]
    [SerializeField, Min(0f)] private float bandShowDelay = 0.58f;

    [Tooltip("봉인 시작 → R 소용돌이 해제(초). 기본 = 사슬이 다 조이는 순간(빌더 SealLockTime).")]
    [SerializeField, Min(0f)] private float sealLockTime = 0.42f;

    [Tooltip("봉인·소용돌이 배율.")]
    [SerializeField, Min(0.01f)] private float sealScale = 1f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private AssassinState state;
    private readonly GameObject[] bands = new GameObject[2];
    private MaterialPropertyBlock block;

    private bool initialized;
    private bool known;          // 마지막으로 본 IsRestrained
    private bool sealPending;    // 켜짐을 봤는데 변신이 끝나길 기다린다
    private float sealAt = -1f;  // 진행 중 봉인 시작 시각
    private EffectHandle vortex = EffectHandle.None;
    private float bandAlpha, bandTarget;

    private void Awake()
    {
        state = GetComponentInParent<AssassinState>();
    }

    // 다시 켜지면 Update 가 봉인 없이 상태를 다시 맞춘다.
    private void OnDisable()
    {
        ReleaseVortex();
        initialized = false;
        sealPending = false;
        sealAt = -1f;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < bands.Length; i++)
            if (bands[i] != null)
                Destroy(bands[i]);
    }

    private void Update()
    {
        if (state == null)
            return;

        bool restrained = state.IsRestrained;
        if (!initialized)
        {
            initialized = true;
            known = restrained;
            SetBandAlpha(restrained ? 1f : 0f);
            bandTarget = bandAlpha;
        }
        else if (restrained != known)
        {
            known = restrained;
            sealPending = restrained;
            if (!restrained)
            {
                CancelSeal();
                bandTarget = 0f;
            }
        }

        if (sealPending && !state.IsTransformed)
            BeginSeal();
        TickSeal();
        TickFade();
    }

    private void BeginSeal()
    {
        sealPending = false;
        sealAt = Time.time;
        SetBandAlpha(0f);
        bandTarget = 0f;

        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;
        Transform body = state.transform;
        EffectEntry seal = effects.Catalog.Assassin_Restraint_Seal;
        if (seal != null)
            effects.Play(seal, body.position, body.rotation, sealScale);
        else
            Edit.LogWarning("[AssassinRestraintView] EffectCatalog.Assassin_Restraint_Seal 이 비어 봉인 연출을 건너뛴다.", this);

        EffectEntry vortexEntry = effects.Catalog.Assassin_R_BladeVortex;
        if (vortexEntry != null)
            vortex = effects.PlayLooping(vortexEntry, body, Vector3.zero, sealScale);
    }

    private void TickSeal()
    {
        if (sealAt < 0f)
            return;
        float elapsed = Time.time - sealAt;
        if (vortex.IsSet && elapsed >= sealLockTime)
            ReleaseVortex();   // 사슬이 다 조였다 — R 이 갇혀 꺼진다(outro _End).
        if (elapsed >= bandShowDelay)
        {
            bandTarget = 1f;
            sealAt = -1f;
        }
    }

    private void CancelSeal()
    {
        sealPending = false;
        sealAt = -1f;
        ReleaseVortex();
    }

    private void ReleaseVortex()
    {
        if (vortex.IsSet && EffectManager.Instance != null)
            EffectManager.Instance.Release(vortex);
        vortex = EffectHandle.None;
    }

    private void TickFade()
    {
        if (Mathf.Approximately(bandAlpha, bandTarget))
            return;
        float length = bandTarget > bandAlpha ? bandFadeIn : bandFadeOut;
        SetBandAlpha(length <= 0f ? bandTarget : Mathf.MoveTowards(bandAlpha, bandTarget, Time.deltaTime / length));
    }

    // 띠는 처음 보일 때 만든다. 재질 _BaseColor 의 알파만 바꾸고(색은 재질 그대로), 0 이면 렌더러를 끈다.
    private void SetBandAlpha(float alpha)
    {
        bandAlpha = alpha;
        if (alpha > 0f)
            EnsureBands();
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < bands.Length; i++)
        {
            if (bands[i] == null)
                continue;
            foreach (Renderer r in bands[i].GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = alpha > 0.001f;
                Material m = r.sharedMaterial;
                Color c = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                c.a *= alpha;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                r.SetPropertyBlock(block);
            }
        }
    }

    private void EnsureBands()
    {
        for (int i = 0; i < bands.Length; i++)
        {
            if (bands[i] != null || i >= daggerSockets.Length || i >= bandPrefabs.Length)
                continue;
            if (daggerSockets[i] == null || bandPrefabs[i] == null)
                continue;
            bands[i] = Instantiate(bandPrefabs[i], daggerSockets[i], false);
        }
    }
}
