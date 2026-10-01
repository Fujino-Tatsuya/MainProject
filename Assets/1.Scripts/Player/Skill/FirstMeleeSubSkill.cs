using UnityEngine;

/// <summary>
/// E 수호자의 의지 — 즉시 자기 자신에게 보호막 부여 (판정 없음).
/// 재사용 시 <b>자기 이전 보호막만</b> 새 보호막으로 교체한다(합산 아님) — <see cref="ShieldType.HolyShield"/> 의 쌓임 규칙이 Replace.
/// 다른 출처의 보호막(거너 Q 등)은 건드리지 않는다. 만료·소진·사망 제거는 Unit 이 처리하고, 이 스킬은 그 통지로 연출만 끝낸다.
/// </summary>
public class FirstMeleeSubSkill : PlayerInstantSkill
{
    // 연출은 PlayerShieldVfx 가 들고 있다 — 이 클래스의 부모(PlayerSkillBase)는 MonoBehaviour 라
    // RPC 를 달 수 없는데, 보호막이 끝나는 지점(Unit.ServerShieldEnded)은 서버 전용이라 전파가 필요하다.
    [Header("연출")]
    [Tooltip("보호막 연출 창구. 플레이어 루트의 PlayerShieldVfx 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private PlayerShieldVfx shieldVfx;

    private bool subscribed;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;

    // 기획서: 즉시 발동이라 이동 제한 없음
    public override bool CanMoveWhileActive => true;

    private FirstMeleeSubSkillData SubSkillData => Data as FirstMeleeSubSkillData;

    private ulong ShieldSourceId => owner.NetworkObjectId;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        FirstMeleeSubSkillData data = SubSkillData;
        if (data == null)
        {
            Debug.LogError("[Player] 수호자의 의지에는 FirstMeleeSubSkillData가 필요합니다.", this);
            return;
        }

        if (!subscribed)
        {
            owner.ServerShieldEnded += OnServerShieldEnded;
            subscribed = true;
        }

        // 재사용 시 교체: 내 이전 보호막을 새 값·새 지속시간으로 덮어쓴다(교체는 종료 통지를 내지 않는다)
        owner.AddShield(ShieldType.HolyShield, ShieldSourceId, data.ShieldAmount, data.ShieldDuration);
        Edit.Log($"[Skill] 수호자의 의지 — 보호막 {data.ShieldAmount} 부여 (지속 {data.ShieldDuration}s)", this);
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 전 피어에서 돈다 — 서버는 직접 경로, 클라는 PlaySkillClientRpc 로 들어온다. RPC 불필요.
        shieldVfx?.PlayLocal();
    }

    // 깨진 것(피해로 소진)과 걷힌 것(시간 만료·사망·추락)은 연출이 다르다. 서버만 이유를 알고 있다.
    private void OnServerShieldEnded(ShieldInstance shield, ShieldEndReason reason)
    {
        if (shield.type != ShieldType.HolyShield || shield.sourceId != ShieldSourceId)
            return;

        shieldVfx?.ServerEnd(reason == ShieldEndReason.Depleted);
    }

    private void OnDestroy()
    {
        if (subscribed && owner != null)
            owner.ServerShieldEnded -= OnServerShieldEnded;
    }
}
