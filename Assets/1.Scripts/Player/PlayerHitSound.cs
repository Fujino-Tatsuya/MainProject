using UnityEngine;

/// <summary>
/// 플레이어 피격 소리(PLAN-sound-mix 3단계 → 11-10 PLAN-sound-spec-apply 3.3). 사운드 명세 <c>Player_Hit</c>:
/// "실제 피해를 받을 때마다, 보호막으로 막아 실제 피해를 받지 않았을 때는 재생하지 않음".
/// <para>
/// <see cref="Unit.ClientDamagedAttributed"/> 의 <b>HP 채널만</b> 본다(경석 Q9 — HP 쪽으로 피해가 들어가면, 체력 하한 클램프 전 값).
/// 피해 RPC 1개 안에서 HP 알림은 많아야 1번이라 보호막+HP 를 함께 깎아도 1번이다. 전 피어에서 불린다.
/// 남이 맞는 소리는 다른 플레이어 배율(30%)이 곱해진다.
/// </para>
/// <para><c>Player.Awake</c> 가 <see cref="PlayerFootstepSound"/> 와 같이 런타임에 붙인다(프리팹 무수정).</para>
/// </summary>
public class PlayerHitSound : MonoBehaviour
{
    private Player _player;

    private void Awake() => _player = GetComponent<Player>();

    private void OnEnable()
    {
        if (_player != null) _player.ClientDamagedAttributed += OnDamaged;
    }

    private void OnDisable()
    {
        if (_player != null) _player.ClientDamagedAttributed -= OnDamaged;
    }

    private void OnDamaged(DamageDealtInfo info)
    {
        if (info.channel != DamageChannel.Hp || info.amount <= 0) return;
        GameSoundHooks.Play(GameSoundHooks.PlayerHit, transform.position, this);
    }
}
