using System.Collections;
using UnityEngine;

/// <summary>
/// E 수호자의 의지 — 즉시 자기 자신에게 보호막 부여 (판정 없음).
/// 재사용 시 기존 보호막을 새 보호막으로 교체한다(합산 아님) — Unit.SetShield가 교체 시맨틱.
/// 보호막은 스킬 종료 후에도 지속시간/사망/소진 전까지 유지되며, 만료는 서버 코루틴이 처리한다.
/// </summary>
public class FirstMeleeSubSkill : PlayerInstantSkill
{
    // 연출은 PlayerShieldVfx 가 들고 있다 — 이 클래스의 부모(PlayerSkillBase)는 MonoBehaviour 라
    // RPC 를 달 수 없는데, 보호막이 끝나는 지점(ExpireShield)은 서버 전용이라 전파가 필요하다.
    [Header("연출")]
    [Tooltip("보호막 연출 창구. 플레이어 루트의 PlayerShieldVfx 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private PlayerShieldVfx shieldVfx;

    private Coroutine expiryRoutine;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Sub;

    // 기획서: 즉시 발동이라 이동 제한 없음
    public override bool CanMoveWhileActive => true;

    private FirstMeleeSubSkillData SubSkillData => Data as FirstMeleeSubSkillData;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        base.OnServerStart(direction, target);

        FirstMeleeSubSkillData data = SubSkillData;
        if (data == null)
        {
            Debug.LogError("[Player] 수호자의 의지에는 FirstMeleeSubSkillData가 필요합니다.", this);
            return;
        }

        // 재사용 시 교체: 남은 수치와 무관하게 새 값으로 덮어쓰고 지속시간도 새로 시작
        owner.SetShield(data.ShieldAmount);
        Edit.Log($"[Skill] 수호자의 의지 — 보호막 {data.ShieldAmount} 부여 (지속 {data.ShieldDuration}s)", this);

        if (expiryRoutine != null)
            StopCoroutine(expiryRoutine);

        expiryRoutine = StartCoroutine(ExpireShield(data.ShieldDuration));
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 전 피어에서 돈다 — 서버는 직접 경로, 클라는 PlaySkillClientRpc 로 들어온다. RPC 불필요.
        shieldVfx?.PlayLocal();
    }

    private IEnumerator ExpireShield(float duration)
    {
        float endTime = Time.time + duration;

        // 깨진 것(피해로 소진)과 걷힌 것(시간 만료·사망)은 연출이 다르다. 서버만 이유를 알고 있으므로
        // 여기서 판정해 ServerEnd 로 넘긴다 — 복제되는 보호막 값만 봐서는 둘을 가를 수 없다.
        bool broken = false;

        // duration이 0 이하면 시간 만료 없이 사망 감시만 한다
        while (duration <= 0f || Time.time < endTime)
        {
            if (owner.CurrentState == PlayerActionState.Dead)
                break;

            // 피해로 먼저 소진된 경우. 이 확인이 없으면 보호막이 이미 깨졌는데도 코루틴이 타이머 끝까지
            // 돌아, 배리어가 남은 시간 내내 떠 있는다(소진은 피격 처리에서 일어나 여기를 지나지 않는다).
            if (owner.CurrentShield <= 0)
            {
                broken = true;
                break;
            }

            yield return null;
        }

        // 자연 소멸 또는 사망 시 즉시 소멸 (소진에 의한 소멸은 피격 처리에서 이미 0)
        owner.SetShield(0);
        expiryRoutine = null;

        shieldVfx?.ServerEnd(broken);
    }
}
