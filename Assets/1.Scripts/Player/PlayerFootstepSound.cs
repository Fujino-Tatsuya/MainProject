using UnityEngine;

/// <summary>
/// 발소리(11-06 플레이테스트) — 걷기 애니메이션 한 바퀴를 <see cref="stepsPerCycle"/> 걸음으로 나눠, 걸음 경계를 넘을 때마다 1번 재생한다.
/// <para>
/// 세 캐릭터 걷기 클립에 발 이벤트가 없어(FBX .meta = SVN) 클립을 고치지 않고 <b>재생 위치(normalizedTime)</b>로 맞춘다 —
/// 애니메이션 속도가 바뀌어도 걸음이 같이 따라간다. 판단은 <c>IsMoving</c> 파라미터(NetworkAnimator 로 전 피어 복제)라 모든 화면에서 각자 울린다.
/// 바닥 재질 판정은 이후 작업 — 지금은 키 하나(<see cref="SoundKey"/>, 콘크리트 5개 랜덤).
/// </para>
/// </summary>
public class PlayerFootstepSound : MonoBehaviour
{
    private const string SoundKey = "S1_Footstep";
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    [Tooltip("걷기 클립 한 바퀴당 걸음 수(왼발·오른발 = 2)")]
    [SerializeField, Min(1)] private int stepsPerCycle = 2;

    [Tooltip("걸음 위치 보정(0~1 바퀴). 발이 땅에 닿는 순간에 맞춘다")]
    [SerializeField, Range(0f, 1f)] private float phaseOffset = 0f;

    private Player _player;
    private int _lastStateHash;
    private int _lastStep = int.MinValue;

    private void Awake() => _player = GetComponent<Player>();

    // 소리 주인 판정 목록(PlayerSoundRegistry) — 모든 플레이어에 붙는 컴포넌트라 여기서 등록한다.
    private void OnEnable() => PlayerSoundRegistry.Register(_player);
    private void OnDisable() => PlayerSoundRegistry.Unregister(_player);

    private void Update()
    {
        Animator animator = _player != null ? _player.CharacterAnimator : null;
        if (animator == null || !animator.isActiveAndEnabled || !animator.GetBool(IsMovingHash))
        {
            _lastStep = int.MinValue;
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.loop)
        {
            _lastStep = int.MinValue;
            return;
        }

        int step = Mathf.FloorToInt((state.normalizedTime + phaseOffset) * stepsPerCycle);
        if (state.shortNameHash != _lastStateHash)
        {
            _lastStateHash = state.shortNameHash;
            _lastStep = step;          // 상태가 바뀐 첫 프레임은 기준만 잡는다
            return;
        }

        if (_lastStep != int.MinValue && step > _lastStep && AudioManager.Instance != null)
            AudioManager.Instance.Play(SoundKey, transform.position, this);   // 남의 발소리 = 30%(Q6)
        _lastStep = step;
    }
}
