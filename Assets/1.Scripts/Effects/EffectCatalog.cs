using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 프로젝트의 EffectEntry를 한 곳에 모아두는 데이터 허브. <see cref="SoundCatalog"/>와 같은 명명 프로퍼티 방식.
///
/// 두 가지 일을 한다:
/// ① 인스펙터가 없는 코드 경로의 룩업 (EffectManager.Instance.Catalog.Grab_Lightning)
/// ② <b>프리워밍 + 빌드 포함 보장</b> — <see cref="All"/>이 프리워밍 목록이고,
///    어떤 씬/프리팹에서도 참조되지 않는 SO는 빌드에서 조용히 빠진다.
///
/// <b>인스펙터로 직접 물려 있는 이펙트도 여기 등록한다.</b> 그쪽은 코드 룩업이 필요 없지만,
/// <b>프리워밍은 이 목록으로만 돈다</b> — 빠져 있으면 그 이펙트의 prewarmCount가 아무 일도 하지 않아
/// 첫 재생에서 인스턴스를 새로 만드느라 한 프레임 튄다. 전투 중 자주 나가는 것일수록 반드시 넣을 것.
///
/// 엔트리를 늘릴 때는 아래에 프로퍼티 한 줄만 추가하면 된다.
/// (<see cref="All"/>는 리플렉션으로 프로퍼티를 훑으므로 목록을 두 곳에 적을 필요가 없다.)
/// </summary>
[CreateAssetMenu(fileName = "EffectCatalog", menuName = "Effects/Effect Catalog")]
public class EffectCatalog : ScriptableObject
{
    // ── 공용 ────────────────────────────────────────────────────────────

    [Header("공용 — 피격 슬롯")]
    // 🔴 이름을 내용으로 바꾸지 말 것(Hit_Flash 등). 이 다섯은 "무엇이 나오는가"가 아니라
    // "몇 번 슬롯인가"다 — HitVFXType 열거형과 1:1이고, 몹 프리팹은 그 값을 정수(hitVFXType: 1)로
    // 직렬화해 들고 있다. 슬롯에 다른 이펙트를 꽂는 순간 내용 기반 이름은 거짓말이 된다.
    // 지금 무엇이 꽂혀 있는지는 F2 디버그 HUD(HitVFXDebugHUD)로 런타임에 돌려 보면 된다.
    [field: SerializeField] public EffectEntry HitEffect1 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect2 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect3 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect4 { get; private set; }
    [field: SerializeField] public EffectEntry HitEffect5 { get; private set; }

    // ── 플레이어 ────────────────────────────────────────────────────────
    // 전부 Paladin_VFX 프리팹이 EffectSocketPlayer / 직접 참조로 물고 있다. 코드 룩업은 쓰지 않지만
    // 프리워밍 대상이라 등록한다 — 기본 공격은 초당 여러 번, 스킬은 전투 내내 나간다.

    [Header("플레이어 — 기본 공격")]
    // O = 적중, X = 허공. 같은 스윙에 둘 중 하나만 나간다.
    [field: SerializeField] public EffectEntry Slash_Hit { get; private set; }
    [field: SerializeField] public EffectEntry Slash_Miss { get; private set; }
    [field: SerializeField] public EffectEntry DoubleSlash_Hit { get; private set; }
    [field: SerializeField] public EffectEntry DoubleSlash_Miss { get; private set; }

    [Header("플레이어 — 불굴의 의지(패시브)")]
    // Heal 은 시전자에게 1회, BonusHit 은 처음 맞은 적 1명에게 1회. FirstMeleePassive 가 같은 RPC 로 함께 뿌린다.
    [field: SerializeField] public EffectEntry Passive_Heal { get; private set; }
    [field: SerializeField] public EffectEntry Passive_BonusHit { get; private set; }

    [Header("플레이어 — 방패 돌진(Q)")]
    [field: SerializeField] public EffectEntry ShieldDash_Trail { get; private set; }
    [field: SerializeField] public EffectEntry ShieldDash_Smash { get; private set; }

    [Header("플레이어 — 수호자의 의지(E)")]
    // Barrier 는 보호막이 떠 있는 동안의 루프, Break 는 피해로 깨질 때만 나가는 원샷이다.
    // 시간 만료로 걷힐 때는 Break 가 나가지 않는다 — 그 구분은 서버만 알고,
    // PlayerShieldVfx 가 ShieldEndReason.Depleted 일 때만 Break 를 퍼뜨린다.
    [field: SerializeField] public EffectEntry HolyShield_Barrier { get; private set; }
    [field: SerializeField] public EffectEntry HolyShield_Break { get; private set; }

    [Header("플레이어 — 남이 걸어 준 보호막(거너 Q 등)")]
    // 위와 같은 물건의 흰색 판. 색만 출처를 말한다 — 금색 = 자기 E, 흰색 = 남이 걸어 준 것.
    // 🔴 **모든 플레이어 캐릭터가 받을 수 있다**(거너 Q 는 자기 자신만 뺀다). 전투 내내 나가므로
    //    프리워밍 대상이다. 배선은 `Tools/VFX/보호막 연출 배선` 이 같이 해 준다.
    [field: SerializeField] public EffectEntry AllyShield_Barrier { get; private set; }
    [field: SerializeField] public EffectEntry AllyShield_Break { get; private set; }

    [Header("플레이어 — 단죄의 방패(우클릭)")]
    // Glow 는 강타 동안의 루프, Wave 는 실제로 맞혔을 때만 터지는 원샷이다(허공 스윙은 조용하다).
    [field: SerializeField] public EffectEntry ShieldInterrupt_Glow { get; private set; }
    [field: SerializeField] public EffectEntry ShieldInterrupt_Wave { get; private set; }

    [Header("플레이어 — 최후의 심판(R)")]
    // 조준 진입 시 SwordLightning + SwordElectric, 시전 확정 시 TargetFloor, 채널 완주 시 Strike.
    // TargetFloor / Strike 는 소켓이 아니라 PlayerSkillVfx 가 엔트리를 직접 들고 재생한다 —
    // 붙을 자리가 내 몸이 아니라 "그때 지목된 남"이라 프리팹에 미리 못 박을 수 없다.
    [field: SerializeField] public EffectEntry Ultimate_SwordLightning { get; private set; }
    [field: SerializeField] public EffectEntry Ultimate_SwordElectric { get; private set; }
    [field: SerializeField] public EffectEntry Ultimate_SwordStick { get; private set; }
    [field: SerializeField] public EffectEntry Ultimate_TargetFloor { get; private set; }
    [field: SerializeField] public EffectEntry Ultimate_Strike { get; private set; }

    // ── 몬스터 ──────────────────────────────────────────────────────────

    [Header("몬스터")]
    [field: SerializeField] public EffectEntry Mob_Slash { get; private set; }
    [field: SerializeField] public EffectEntry Mob_Bite { get; private set; }

    // ── 보스 ────────────────────────────────────────────────────────────

    [Header("보스 — 23호 낙하")]
    [field: SerializeField] public EffectEntry Drop_Charge_Boundary { get; private set; }
    [field: SerializeField] public EffectEntry Drop_Charge_Indicator { get; private set; }
    // 이름은 "충돌"이지만 실체는 착지 먼지(FX_JumpDrop_Dust)다. 호출부가 낙하 충돌 시점에 부른다.
    [field: SerializeField] public EffectEntry Drop_Collision { get; private set; }

    // 착지 연출(FX_JumpLanding). 프리팹의 EffectSocketPlayer(id: JumpLanding)가 물고 있고
    // 클립 이벤트가 그 id 로 지목한다 — 코드 룩업은 안 쓰지만 프리워밍 목록이라 등록한다.
    // 낙하 먼지(Drop_Collision)와 별개다 — 그쪽은 코드가 낙하 충돌 시점에 따로 부른다.
    // (예전 에셋 이름은 FX_Rage_Smash 였다 — 레이지와 무관한데 그 이름이라 바꿨다.)
    [field: SerializeField] public EffectEntry Drop_JumpLanding { get; private set; }

    [Header("보스 — 잡기 체인")]
    [field: SerializeField] public EffectEntry Grab_Lightning { get; private set; }
    [field: SerializeField] public EffectEntry Grab_ArmElectric { get; private set; }
    [field: SerializeField] public EffectEntry Grabbed_Electric { get; private set; }
    // 던지는 순간의 연출. 예전 이름은 그냥 Throw 였는데 잡기 체인의 일부라는 게 안 보여서 접두사를 맞췄다.
    [field: SerializeField] public EffectEntry Grab_Throw { get; private set; }
    [field: SerializeField] public EffectEntry Punch_HitSpark { get; private set; }

    [Header("보스 — 웰즈 폭탄")]
    // 폭탄이 **움직이는 동안**만 흐르는 루프(FX_Wells_Bomb_Trail). BossBomb 이 상태 전이에서 켜고 끈다.
    [field: SerializeField] public EffectEntry Wells_Bomb_Trail { get; private set; }

    // 폭탄 폭발 원샷(FX_Bomb_Explode). 장판과 별개다 — 이건 터지는 **순간**의 연출이다.
    [field: SerializeField] public EffectEntry Bomb_Explode { get; private set; }

    // 폭발 **뒤에 남는 장판**의 비주얼(FX_Bomb_Exploded). 장판의 수명·반경은 AreaZone 이 정하고
    // 이 엔트리는 그 위에 얹히는 그림이다 — 피해 판정은 여기에 들어 있지 않다.
    [field: SerializeField] public EffectEntry Bomb_Exploded { get; private set; }

    [Header("보스 — 차징 번개구슬 (4단계)")]
    // 한 엔트리에 파트로 몰지 않고 넷으로 나눈 이유: FadeOut과 Break가 서로 다른 종료 분기다.
    // 하나로 묶으면 "어느 쪽으로 끝났는지"를 데이터가 표현할 수 없다.
    [field: SerializeField] public EffectEntry ChargeBall_Grow { get; private set; }
    [field: SerializeField] public EffectEntry ChargeBall_Loop { get; private set; }
    [field: SerializeField] public EffectEntry ChargeBall_FadeOut { get; private set; }
    [field: SerializeField] public EffectEntry ChargeBall_Break { get; private set; }

    [Header("보스 — 돌진 전기")]
    // 23호가 일반 돌진·레이지 돌진을 할 때 몸통에서 손발로 흐르는 전기(EffectPathPlayer 4갈래).
    // 그랩 팔 전기와 **파트 프리팹은 같고 엔트리만 다르다** — 그쪽은 1갈래, 이쪽은 4갈래라
    // prewarmCount 수요가 다르다(4 vs 24). 엔트리를 공유하면 한쪽 튜닝이 다른 쪽을 흔든다.
    [field: SerializeField] public EffectEntry Dash_LimbElectric { get; private set; }

    [Header("보스 — 레이지")]
    // 레이지 돌진 동안 몸에 두르는 루프(FX_Rage_Shield). TwentyThreeBoss 의 rageSmash 필드가
    // 물고 있는 그것이다 — 필드 이름만 Smash 인 채로 남아 있다.
    [field: SerializeField] public EffectEntry Rage_Shield { get; private set; }

    // ── 부술 수 있는 오브젝트 ──────────────────────────────────────────

    [Header("오브젝트 — 상자")]
    [field: SerializeField] public EffectEntry Explosion_Basic { get; private set; }
    // 미리 구워 둔 파편 덩어리(FX_Frag_Box_01_Burst). 에셋 이름이 Box 라 Crate 였던 이름을 맞췄다.
    [field: SerializeField] public EffectEntry Frag_Box_01 { get; private set; }

    private List<EffectEntry> _all;
    // 피격 이펙트 테스트용 enum
    public enum HitVFXType
    {
        HitEffect1,
        HitEffect2,
        HitEffect3,
        HitEffect4,
        HitEffect5
    }

    public EffectEntry GetHitEffect(HitVFXType hitVFX)
    {
        EffectEntry effectEntry = HitEffect1;
        switch (hitVFX)
        {
            case HitVFXType.HitEffect1:
                break;
            case HitVFXType.HitEffect2:
                effectEntry = HitEffect2;
                break;
            case HitVFXType.HitEffect3:
                effectEntry = HitEffect3;
                break;
            case HitVFXType.HitEffect4:
                effectEntry = HitEffect4;
                break;
            case HitVFXType.HitEffect5:
                effectEntry = HitEffect5;
                break;
        }
        return effectEntry;
    }

    /// <summary>
    /// 이 카탈로그가 들고 있는 모든 엔트리(중복·null 제거). 프리워밍이 이 목록을 쓴다.
    /// 최초 접근 시 한 번만 리플렉션으로 만든다.
    /// </summary>
    public IReadOnlyList<EffectEntry> All
    {
        get
        {
            // 빈 결과는 캐시하지 않는다. 에디터는 도메인 리로드 설정에 따라 이 인스턴스의 관리 상태를
            // 살려두기도 하는데, 그때 "비어 있던 시점의 캐시"가 눌러앉으면 프리워밍이 조용히 통째로 빠진다.
            if (_all == null || _all.Count == 0) Rebuild();
            return _all;
        }
    }

    private void Rebuild()
    {
        _all = new List<EffectEntry>();

        PropertyInfo[] properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < properties.Length; i++)
        {
            if (properties[i].PropertyType != typeof(EffectEntry)) continue;

            var entry = properties[i].GetValue(this) as EffectEntry;
            if (entry != null && !_all.Contains(entry)) _all.Add(entry);
        }
    }
}
