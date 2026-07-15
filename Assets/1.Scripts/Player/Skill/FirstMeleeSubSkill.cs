using System.Collections;
using UnityEngine;

/// <summary>
/// E 수호자의 의지 — 즉시 자기 자신에게 보호막 부여 (판정 없음).
/// 재사용 시 기존 보호막을 새 보호막으로 교체한다(합산 아님) — Unit.SetShield가 교체 시맨틱.
/// 보호막은 스킬 종료 후에도 지속시간/사망/소진 전까지 유지되며, 만료는 서버 코루틴이 처리한다.
/// </summary>
public class FirstMeleeSubSkill : PlayerInstantSkill
{
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

        if (expiryRoutine != null)
            StopCoroutine(expiryRoutine);

        expiryRoutine = StartCoroutine(ExpireShield(data.ShieldDuration));
    }

    public override void OnClientPlay(Vector3 direction)
    {
        // 보호막 생성/파괴/자연 소멸 연출은 VFX 확정 후 추가
    }

    private IEnumerator ExpireShield(float duration)
    {
        float endTime = Time.time + duration;

        // duration이 0 이하면 시간 만료 없이 사망 감시만 한다
        while (duration <= 0f || Time.time < endTime)
        {
            if (owner.CurrentState == PlayerActionState.Dead)
                break;

            yield return null;
        }

        // 자연 소멸 또는 사망 시 즉시 소멸 (소진에 의한 소멸은 피격 처리에서 이미 0)
        owner.SetShield(0);
        expiryRoutine = null;
    }
}
