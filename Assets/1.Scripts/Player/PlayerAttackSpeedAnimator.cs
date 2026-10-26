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
/// [전 피어] 플레이어 Animator 의 공격속도 쓰기 지점(할 일 91 T5·A5) — 두 가지를 쓴다.
///   · Animator.speed: 창구 중 하나라도 공격속도를 타면 배율, 아니면 1. 대시가 0층(Dodge)을 쥔 동안은 1.
///   · 상태 속도 파라미터 <see cref="RateParameter"/>: 항상 배율. 일부 상태만 공속을 타는 동작(팔라딘 E 준비·해제·반격 — 방어중 제외)이
///     애니메이터 상태의 Speed Multiplier 로 이 파라미터를 켠다. 파라미터가 없는 컨트롤러는 건너뛴다(기본값 1).
/// 배율은 복제된 상태이상으로 각 피어가 계산하므로 서버·오너·관찰자 재생 속도가 같다. 바뀔 때만 쓴다.
/// 다른 Update 의 상태 변화(스킬 시작·종료, 대시)를 같은 프레임에 반영하도록 늦게 돈다 — 애니메이터 평가는 모든 Update 뒤다.
/// Player.Awake 가 붙인다(프리팹 수정 불필요).
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class PlayerAttackSpeedAnimator : MonoBehaviour
{
    /// <summary>상태 Speed Multiplier 용 float 파라미터 이름. 컨트롤러 기본값은 1 로 둔다.</summary>
    public const string RateParameter = "AttackSpeedRate";
    private static readonly int RateParameterHash = Animator.StringToHash(RateParameter);

    private Player player;
    private PlayableCharacterVisual visual;
    private IPlayerAttackSpeedAnimatorUser[] users;
    private PlayerDashController dash;
    private Animator animator;
    private float appliedSpeed = float.NaN;
    // 파라미터 유무는 컨트롤러마다 한 번만 본다(animator.parameters 는 매번 배열을 만든다).
    private RuntimeAnimatorController rateParameterCheckedFor;
    private bool hasRateParameter;

    private void Awake()
    {
        player = GetComponent<Player>();
        users = GetComponents<IPlayerAttackSpeedAnimatorUser>();
        dash = GetComponent<PlayerDashController>();
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

        float rate = player != null ? player.AttackSpeedRate : 1f;
        WriteRateParameter(PlayerAttackSpeedRules.Rate(rate));

        // 대시(Dodge)가 0층을 쥔 동안은 공속을 풀어 1 — Dodge 는 DodgeSpeed 로 대시 시간에 맞추므로 Animator.speed 가 곱해지면 어긋난다(할 일 92-5).
        // 관찰자도 Dodge 재생 여부로 같은 판정을 한다(늦게 오는 평타 CancelRpc 와 무관).
        bool dodging = dash != null && dash.HoldsBasePose;
        bool scales = false;
        for (int i = 0; i < users.Length && !dodging; i++)
        {
            if (users[i] != null && users[i].ScalesAnimatorWithAttackSpeed)
            {
                scales = true;
                break;
            }
        }

        float speed = PlayerAttackSpeedRules.AnimatorSpeed(scales, rate);
        if (speed == appliedSpeed)
            return;

        animator.speed = speed;
        appliedSpeed = speed;
    }

    // 현재 값과 비교해 쓴다 — 오너 권한 NetworkAnimator 가 파라미터를 덮어써도 같은 값으로 되돌아온다.
    private void WriteRateParameter(float rate)
    {
        // 초기화 전 animator.parameters 는 비어 있다 — 그때 본 결과를 캐시하지 않는다.
        if (!animator.isInitialized)
            return;

        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller != rateParameterCheckedFor)
        {
            rateParameterCheckedFor = controller;
            hasRateParameter = controller != null && HasFloatParameter(animator, RateParameterHash);
        }

        if (hasRateParameter && animator.GetFloat(RateParameterHash) != rate)
            animator.SetFloat(RateParameterHash, rate);
    }

    private static bool HasFloatParameter(Animator target, int hash)
    {
        foreach (AnimatorControllerParameter parameter in target.parameters)
        {
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Float)
                return true;
        }

        return false;
    }

    // 외형 교체로 애니메이터가 바뀌면 다음 Update 에서 새 것을 찾아 다시 쓴다 — 옛 외형의 Destroy 는 프레임 끝에 끝난다.
    private void HandleCharacterApplied(CharacterDefinition _) => animator = null;

    private void ResolveAnimator()
    {
        animator = GetComponentInChildren<Animator>();
        appliedSpeed = float.NaN;
        rateParameterCheckedFor = null;
        hasRateParameter = false;
    }
}
