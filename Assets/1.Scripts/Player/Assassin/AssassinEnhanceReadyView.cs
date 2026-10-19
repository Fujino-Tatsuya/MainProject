using UnityEngine;

/// <summary>
/// 어쌔신 일반 E(단검 강화) 버프 연출 — 버프가 남아 있는 동안 양손 단검 날이 붉게 빛나고 검붉은 오라가 일렁인다(2026-10-16 민경).
/// <c>Assassin_Armature/VFX/EnhancedReady</c> 에 붙는다(단검·소켓이 같은 프리팹 안에 있어 참조가 한 겹으로 끝난다).
///
/// - 켜짐/꺼짐 = <see cref="AssassinState.IsEnhancedReady"/>(서버 쓰기 NetworkVariable, 전 피어 같은 값). 추가 네트워크 없음.
///   버프는 지속시간이 없다 — 강화 일격 시작(서버 수락)·변신·쓰러짐·사망에 꺼진다. 어느 쪽이든 같은 꺼짐 연출.
/// - 차지 버스트는 <see cref="AssassinState.EnhancedReadyChanged"/>(true) 순간에만 낸다. 늦은 접속·Armature 재활성처럼
///   이벤트 없이 상태만 맞추는 경우는 <see cref="Update"/> 가 차지 없이 바로 켠다.
/// - 날 발광 = 팔라딘 패시브와 같은 방식 — 단검 렌더러의 오버레이 슬롯 [1] <c>Dagger_Blade_Glow</c>
///   (칼날 마스크 텍스처 <c>Dagger_BladeMask</c>)를 <see cref="MaterialFadeEffect"/> 가 <c>_ColorFactor</c> 0 ↔ 1 로 페이드.
/// 프리팹이 비어 있으면 해당 연출만 빠진다.
/// </summary>
public sealed class AssassinEnhanceReadyView : MonoBehaviour
{
    [Tooltip("단검 날 발광 — 같은 오브젝트의 MaterialFadeEffect(오버레이 Dagger_Blade_Glow 의 _ColorFactor).")]
    [SerializeField] private MaterialFadeEffect bladeGlow;

    [Tooltip("오라·차지를 붙일 단검 소켓 — Dagger_Weapon_R/L 의 VFX_EnhanceAura(날 중간, 로컬 +Y = 칼끝 방향).")]
    [SerializeField] private Transform[] bladeSockets = new Transform[2];

    [Tooltip("E 입력 순간 단검으로 빨려 드는 차지(원샷) — 소켓마다 1개.")]
    [SerializeField] private GameObject chargePrefab;
    [Tooltip("버프 동안 단검을 따라 일렁이는 오라(루프) — 소켓마다 1개.")]
    [SerializeField] private GameObject auraPrefab;
    [Tooltip("차지 원샷·꺼진 오라가 남은 입자를 흘리고 사라지기까지(초).")]
    [SerializeField, Min(0.1f)] private float releaseLifetime = 1.5f;

    private AssassinState state;
    private GameObject[] auras;
    private bool shown;

    private void Awake()
    {
        state = GetComponentInParent<AssassinState>();
        if (state != null)
            state.EnhancedReadyChanged += HandleEnhancedReadyChanged;
        auras = new GameObject[bladeSockets != null ? bladeSockets.Length : 0];
    }

    private void OnDestroy()
    {
        if (state != null)
            state.EnhancedReadyChanged -= HandleEnhancedReadyChanged;
    }

    // 유령 중 Armature 가 꺼지면 같이 꺼진다 — 오라는 바로 걷고, 다시 켜지면 Update 가 차지 없이 맞춘다.
    private void OnDisable()
    {
        shown = false;
        if (auras == null)
            return;
        for (int i = 0; i < auras.Length; i++)
        {
            if (auras[i] != null)
                Destroy(auras[i]);
            auras[i] = null;
        }
    }

    private void Update()
    {
        if (state != null && state.IsEnhancedReady != shown)
            Apply(state.IsEnhancedReady, false);
    }

    private void HandleEnhancedReadyChanged(bool on)
    {
        if (isActiveAndEnabled)
            Apply(on, true);
    }

    private void Apply(bool on, bool playCharge)
    {
        if (on == shown)
            return;
        shown = on;

        if (on)
        {
            if (bladeGlow != null)
                bladeGlow.FadeIn();
            for (int i = 0; i < auras.Length; i++)
            {
                Transform socket = bladeSockets[i];
                if (socket == null)
                    continue;
                if (playCharge)
                    Spawn(chargePrefab, socket, releaseLifetime);
                if (auras[i] == null)
                    auras[i] = Spawn(auraPrefab, socket, 0f);
            }
            return;
        }

        if (bladeGlow != null)
            bladeGlow.FadeOut();
        for (int i = 0; i < auras.Length; i++)
        {
            GameObject aura = auras[i];
            auras[i] = null;
            if (aura == null)
                continue;
            // 방출만 멈춘다 — World 로 흘러간 입자는 제 수명대로 사라지게 둔다.
            foreach (ParticleSystem particle in aura.GetComponentsInChildren<ParticleSystem>())
                particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(aura, releaseLifetime);
        }
    }

    // lifetime 0 = 루프(호출자가 걷는다).
    private static GameObject Spawn(GameObject prefab, Transform socket, float lifetime)
    {
        if (prefab == null)
            return null;
        GameObject instance = Instantiate(prefab, socket, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        if (lifetime > 0f)
            Destroy(instance, lifetime);
        return instance;
    }
}
