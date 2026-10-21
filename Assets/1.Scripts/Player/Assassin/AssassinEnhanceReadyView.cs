using UnityEngine;

/// <summary>
/// 어쌔신 일반 E(단검 강화) 버프 연출 — 버프가 남아 있는 동안 양손 단검 날이 붉게 빛나고 검붉은 오라가 일렁인다(2026-10-16 민경, 10-20 정식 VFX 배선).
/// <c>Player_Assassin/VFX/NormalE</c> 에 붙는다(10-20 민경 — 오라·차지 소켓 플레이어와 같은 곳). 같은 오브젝트의 <see cref="MaterialFadeEffect"/> 대상은
/// Armature 안 양손 단검 렌더러 2개를 직접 지정한다(비우면 자기 자식에서 찾는데 여기엔 단검이 없다).
///
/// - 켜짐/꺼짐 = <see cref="AssassinState.IsEnhancedReady"/>(서버 쓰기 NetworkVariable, 전 피어 같은 값). 추가 네트워크 없음.
///   버프는 지속시간이 없다 — 강화 일격 시작(서버 수락)·변신·쓰러짐·사망에 꺼진다. 어느 쪽이든 같은 꺼짐 연출.
/// - 차지 버스트는 <see cref="AssassinState.EnhancedReadyChanged"/>(true) 순간에만 낸다. 늦은 접속·Armature 재활성처럼
///   이벤트 없이 상태만 맞추는 경우는 <see cref="Update"/> 가 차지 없이 바로 켠다.
/// - 오라·차지 = <c>VFX/NormalE/Aura_R·Aura_L</c> 의 <see cref="EffectSocketPlayer"/>(Socket = 단검 <c>Dagger_Weapon_R/L/VFX_EnhanceAura</c>, 칼날 중간) —
///   엔트리 <c>EffectCatalog.Assassin_E_Aura</c>(루프, 끄면 outro 동안 흩어짐)·<c>Assassin_E_Charge</c>(원샷). 무기를 따라간다.
///   소켓 플레이어는 뼈대 배율을 곱하지 않으므로 단검 배율(1.25)은 각 플레이어의 Scale 로 맞춘다. 오라는 Safety Timeout 0(버프 무기한).
/// - 날 발광 = 팔라딘 패시브와 같은 방식 — 단검 렌더러의 오버레이 슬롯 [1] <c>Dagger_Blade_Glow</c>
///   (칼날 마스크 텍스처 <c>Dagger_BladeMask</c>)를 <see cref="MaterialFadeEffect"/> 가 <c>_ColorFactor</c> 0 ↔ 1 로 페이드.
/// 비어 있는 칸은 해당 연출만 빠진다.
/// </summary>
public sealed class AssassinEnhanceReadyView : MonoBehaviour
{
    [Tooltip("단검 날 발광 — 같은 오브젝트의 MaterialFadeEffect(오버레이 Dagger_Blade_Glow 의 _ColorFactor).")]
    [SerializeField] private MaterialFadeEffect bladeGlow;

    [Tooltip("버프 동안 단검을 따라 일렁이는 오라(루프) — 단검마다 1개(VFX/NormalE/Aura_R·Aura_L/Aura, [0] = 오른손).")]
    [SerializeField] private EffectSocketPlayer[] auras = new EffectSocketPlayer[2];

    [Tooltip("E 로 강화되는 순간 단검으로 빨려 드는 차지(원샷) — 단검마다 1개(VFX/NormalE/Aura_R·Aura_L/Charge, [0] = 오른손).")]
    [SerializeField] private EffectSocketPlayer[] charges = new EffectSocketPlayer[2];

    private AssassinState state;
    private bool shown;

    private void Awake()
    {
        state = GetComponentInParent<AssassinState>();
        if (state != null)
            state.EnhancedReadyChanged += HandleEnhancedReadyChanged;
    }

    private void OnDestroy()
    {
        if (state != null)
            state.EnhancedReadyChanged -= HandleEnhancedReadyChanged;
    }

    // 이 오브젝트가 꺼졌다 켜지면 Update 가 차지 없이 상태를 다시 맞춘다(오라는 소켓 플레이어의 OnDisable 이 회수).
    private void OnDisable() => shown = false;

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
            if (playCharge)
                ForEach(charges, c => c.PlayOnce());
            ForEach(auras, a => { if (!a.IsPlaying) a.Play(); });
            return;
        }

        if (bladeGlow != null)
            bladeGlow.FadeOut();
        // 엔트리 outroDuration 동안 방출을 멈추고 남은 입자를 흩어지게 한 뒤 반납한다.
        ForEach(auras, a => { if (a.IsPlaying) a.Stop(); });
    }

    private static void ForEach(EffectSocketPlayer[] players, System.Action<EffectSocketPlayer> action)
    {
        if (players == null)
            return;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                action(players[i]);
        }
    }
}
