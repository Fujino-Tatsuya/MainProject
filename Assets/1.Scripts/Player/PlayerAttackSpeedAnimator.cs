using UnityEngine;

/// <summary>
/// 공격속도를 Animator.speed 로 쓰는 동작의 창구(할 일 91 T5). 지금 그 동작 중이면 true.
/// 구현 = 스킬 컨트롤러(이벤트 방식 스킬 실행 중) · 어쌔신 평타 · 거너 평타. 가붕이 콤보는 상태 파라미터(AttackSpeed)를 따로 쓴다.
/// </summary>
public interface IPlayerAttackSpeedAnimatorUser
{
    bool ScalesAnimatorWithAttackSpeed { get; }
}

/// <summary>
/// [전 피어] 플레이어 Animator.speed 의 유일한 쓰기 지점(할 일 91 T5) — 창구 중 하나라도 공격속도를 타면 배율, 아니면 1.
/// 배율은 복제된 상태이상으로 각 피어가 계산하므로 서버·오너·관찰자 재생 속도가 같다. 바뀔 때만 쓴다.
/// 다른 Update 의 상태 변화(스킬 시작·종료, 대시)를 같은 프레임에 반영하도록 늦게 돈다 — 애니메이터 평가는 모든 Update 뒤다.
/// Player.Awake 가 붙인다(프리팹 수정 불필요).
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class PlayerAttackSpeedAnimator : MonoBehaviour
{
    private Player player;
    private PlayableCharacterVisual visual;
    private IPlayerAttackSpeedAnimatorUser[] users;
    private Animator animator;
    private float appliedSpeed = float.NaN;

    private void Awake()
    {
        player = GetComponent<Player>();
        users = GetComponents<IPlayerAttackSpeedAnimatorUser>();
        visual = GetComponent<PlayableCharacterVisual>();
        if (visual != null)
            visual.CharacterApplied += HandleCharacterApplied;
        ResolveAnimator();
    }

    private void OnDestroy()
    {
        if (visual != null)
            visual.CharacterApplied -= HandleCharacterApplied;
    }

    private void Update()
    {
        if (animator == null)
            ResolveAnimator();
        if (animator == null)
            return;

        bool scales = false;
        for (int i = 0; i < users.Length; i++)
        {
            if (users[i] != null && users[i].ScalesAnimatorWithAttackSpeed)
            {
                scales = true;
                break;
            }
        }

        float speed = PlayerAttackSpeedRules.AnimatorSpeed(scales, player != null ? player.AttackSpeedRate : 1f);
        if (speed == appliedSpeed)
            return;

        animator.speed = speed;
        appliedSpeed = speed;
    }

    // 외형 교체로 애니메이터가 바뀌면 다음 Update 에서 새 것을 찾아 다시 쓴다 — 옛 외형의 Destroy 는 프레임 끝에 끝난다.
    private void HandleCharacterApplied(CharacterDefinition _) => animator = null;

    private void ResolveAnimator()
    {
        animator = GetComponentInChildren<Animator>();
        appliedSpeed = float.NaN;
    }
}
