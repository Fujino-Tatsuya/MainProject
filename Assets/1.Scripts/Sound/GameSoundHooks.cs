using Ami.BroAudio;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 사운드 적용 명세(Sound_2 v0.1)대로 <b>코드 사건</b>에 붙인 소리의 키·공용 판정 — PLAN-sound-spec-apply 3단계.
/// 호출부는 한 줄(<c>GameSoundHooks.Play(...)</c>)만 두고, 키 이름과 "누가 언제 몇 번" 규칙은 여기 모은다.
/// <para>
/// 🔴 모든 호출부는 <b>전 피어가 지나는 곳</b>(ClientRpc·복제값 콜백·애니 이벤트)이어야 한다 — 소리는 각 PC 가 따로 낸다.
/// 서버 가드 뒤에 두면 호스트만 듣는다. 호스트는 서버+클라를 겸하므로 한 사건에 한 경로만.
/// </para>
/// </summary>
public static class GameSoundHooks
{
    // ── 공용 전투 ──
    public const string MonsterHit = "S1_Monster_Hit";
    public const string MonsterDead = "S1_Monster_Dead";
    public const string PlayerHit = "S1_Player_Hit";
    public const string InterruptStart = "S1_Interrupt_Start";
    public const string Dash = "S1_Dash";

    // ── 팔라딘 ──
    public const string PaladinQHit = "S1_Paladin_Q";
    public const string PaladinQLoop = "S1_Paladin_Q_Loop";
    public const string PaladinR = "S1_Paladin_R";
    public const string PaladinEFail = "S1_Paladin_E_Fail";
    static readonly string[] PaladinAttack = { "S1_Paladin_Attack_1", "S1_Paladin_Attack_2", "S1_Paladin_Attack_3", "S1_Paladin_Attack_4" };

    // ── 어쌔신 ──
    public const string AssassinQ = "S1_Assassin_Q";
    public const string AssassinEStart = "S1_Assassin_E_Start";
    public const string AssassinRStart = "S1_Assassin_R_Start";
    public const string AssassinREnd = "S1_Assassin_R_End";
    public const string AssassinAttackTransformed = "S1_Assassin_Attack_Tf";
    static readonly string[] AssassinAttack = { "S1_Assassin_Attack_1", "S1_Assassin_Attack_2", "S1_Assassin_Attack_3", "S1_Assassin_Attack_4" };

    // ── 거너 ──
    public const string GunnerQCancel = "S1_Gunner_Q_Cancel";
    public const string GunnerR = "S1_Gunner_R";

    // ── 몬스터·23호 ──
    public const string Punch = "S1_Punch_Hit";
    public const string SpinnerSpin = "S1_Spinner_Spin";
    public const string No23GrabStart = "S1_23_Grab_Start";
    public const string No23GrabFloor = "S1_23_Grab_Floor";
    public const string No23Drone = "S1_23_Drone_Explode";
    public const string InterruptWall = "S1_Interrupt_Wall";

    /// <summary>팔라딘 평타 0기반 타수 → 키. 범위 밖이면 마지막 타.</summary>
    public static string PaladinAttackKey(int attackIndex) => PaladinAttack[Mathf.Clamp(attackIndex, 0, PaladinAttack.Length - 1)];

    /// <summary>어쌔신 일반 평타 1기반 타수 → 키.</summary>
    public static string AssassinAttackKey(int step) => AssassinAttack[Mathf.Clamp(step - 1, 0, AssassinAttack.Length - 1)];

    public static IAudioPlayer Play(string key, Vector3 position, Component source = null)
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null) return null;
        return source != null ? audio.Play(key, position, source) : audio.Play(key, position);
    }

    // ── 완성음 정렬(파일 안 충돌음 위치, 11-10 파형 측정 — scratchpad wavpeaks.py, 10ms RMS 최댓값 근처) ──
    /// <summary>Paladin_R_Skill.wav(3.0초) 안의 "땅에 꽂히는" 충돌음 위치(초). 0.95~1.10초가 가장 크다.</summary>
    public const float PaladinRFileImpact = 1.0f;
    /// <summary>팔라딘 R 클립의 피해(Hit) 이벤트 시각 — 클립 배속 1 기준(초). 실제 시각 = 이 값 ÷ (animationSpeed × beforeHitSpeedMultiplier).</summary>
    public const float PaladinRHitClipTime = 1.367f;
    /// <summary>23_Drone.wav(1.652초)를 폭발 몇 초 전에 시작하는가 — 명세 "실제 폭발 약 0.5초 전". 청취로 조정.</summary>
    public const float DroneLeadSeconds = 0.5f;

    /// <summary>
    /// 파일 안 충돌음(<paramref name="fileImpact"/>초)이 지금부터 <paramref name="secondsUntilImpact"/>초 뒤에 울리게 재생한다(Q11).
    /// 남은 시간이 더 길면 그만큼 늦게 시작, 짧으면 파일 앞부분을 건너뛴다.
    /// </summary>
    public static IAudioPlayer PlayAligned(string key, Vector3 position, Component source, float secondsUntilImpact, float fileImpact)
    {
        IAudioPlayer player = Play(key, position, source);
        if (player == null) return null;
        float lead = secondsUntilImpact - fileImpact;
        return lead >= 0f ? player.SetDelay(lead) : player.SetStartOffset(-lead);
    }

    public static void Stop(string key, float fadeOut = 0.1f)
    {
        AudioManager.Instance?.StopKey(key, fadeOut);
    }

    /// <summary>재생 중인 개별 핸들만 끈다(같은 키의 다른 인스턴스는 그대로). 재활용된 핸들은 건너뛴다.</summary>
    public static void Stop(IAudioPlayer player, float fadeOut = 0.1f)
    {
        if (player != null && player.IsActive) player.Stop(fadeOut);
    }

    /// <summary>
    /// [전 피어] clientId 의 플레이어. <c>SpawnManager.GetPlayerNetworkObject</c> 는 클라에서 남의 것을 찾으면 에러를 찍으므로
    /// 스폰 목록을 직접 훑는다(<c>InterruptProductionDirector.FindSuccessFxSinks</c> 와 같은 방식). 드문 사건(팔라딘 Q 피해)에만 쓴다.
    /// </summary>
    public static bool TryFindPlayer(ulong clientId, out Player player)
    {
        player = null;
        if (clientId == ulong.MaxValue) return false;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return false;

        foreach (NetworkObject networkObject in nm.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject == null || networkObject.OwnerClientId != clientId) continue;
            if (networkObject.TryGetComponent(out player) && player != null) return true;
        }

        player = null;
        return false;
    }

    /// <summary>팔라딘인가 — 팔라딘 Q 스킬(<see cref="FirstMeleeMainSkill"/>)을 가졌는가.</summary>
    public static bool IsPaladin(Player player) =>
        player != null && player.GetComponentInChildren<FirstMeleeMainSkill>(true) != null;
}

/// <summary>
/// [전 피어] 몬스터 피격음 — 피해 RPC 1개(= 타격 1건)당 1번. 보호막+HP 를 한 RPC 로 받으면 1번(사운드 명세 + Q2·Q8).
/// <para>
/// 🔴 23호 자폭 실드 초과 타격은 서버가 HP RPC 와 실드 RPC 를 <b>따로</b> 보낸다(<c>ReportAbsorbedShieldDamage</c>).
/// 같은 프레임에 같은 공격자의 HP 타격 바로 뒤에 오는 "실드만" 보고 1건은 같은 타격이므로 소리를 내지 않는다.
/// 시간 창 중복 제거가 아니다 — 정상 다단히트(각 RPC 에 HP 가 있음)는 그대로 다 울린다.
/// </para>
/// </summary>
public sealed class MonsterHitSoundTracker
{
    int _pendingHp;
    int _pendingShield;
    AttackType _pendingType;

    int _lastHpHitFrame = -1;
    ulong _lastHpHitAttacker = ulong.MaxValue;

    /// <summary><c>Unit.ClientDamagedAttributed</c> — 같은 RPC 안에서 <see cref="OnHit"/> 보다 먼저 온다.</summary>
    public void OnDamaged(DamageDealtInfo info)
    {
        if (info.channel == DamageChannel.Hp) _pendingHp += info.amount;
        else _pendingShield += info.amount;
        _pendingType = info.attackType;
    }

    /// <summary><c>Unit.ClientHitAttributed</c> — RPC 의 마지막. 소리를 낼지 판정하고 대기값을 비운다.</summary>
    public void OnHit(ulong attackerClientId, Component target)
    {
        int hp = _pendingHp;
        int shield = _pendingShield;
        AttackType type = _pendingType;
        _pendingHp = 0;
        _pendingShield = 0;

        // 플레이어가 준 피해만(몬스터끼리·환경 피해 제외).
        if (attackerClientId == ulong.MaxValue || target == null) return;

        int frame = Time.frameCount;
        if (hp <= 0 && shield > 0 && frame == _lastHpHitFrame && attackerClientId == _lastHpHitAttacker)
        {
            _lastHpHitFrame = -1;   // 한 번만 흡수 — 같은 프레임의 다음 실드 타격은 별개 타격으로 울린다
            return;
        }

        if (hp > 0)
        {
            _lastHpHitFrame = frame;
            _lastHpHitAttacker = attackerClientId;
        }

        Vector3 position = target.transform.position;
        GameSoundHooks.Play(GameSoundHooks.MonsterHit, position);

        // 팔라딘 Q 피격음(Q10) — 팔라딘의 SkillQ 피해 전부(돌파의 방패·진격의 충격 등 파생 포함). 공용 피격음과 별개 레이어.
        if (type == AttackType.SkillQ && GameSoundHooks.TryFindPlayer(attackerClientId, out Player attacker) &&
            GameSoundHooks.IsPaladin(attacker))
            GameSoundHooks.Play(GameSoundHooks.PaladinQHit, position, attacker);
    }
}
