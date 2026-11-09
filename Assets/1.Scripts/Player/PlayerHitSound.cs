using UnityEngine;

/// <summary>
/// 플레이어 피격 소리(PLAN-sound-mix 3단계). <see cref="Unit.ClientHitAttributed"/> = 같은 피해 RPC 를 타격 1회로 한 번만 알림
/// (보호막+체력을 함께 깎아도 1회) — 전 피어에서 불린다. 남이 맞는 소리는 다른 플레이어 배율(30%)이 곱해진다.
/// <para><c>Player.Awake</c> 가 <see cref="PlayerFootstepSound"/> 와 같이 런타임에 붙인다(프리팹 무수정).</para>
/// </summary>
public class PlayerHitSound : MonoBehaviour
{
    private const string SoundKey = "S1_Player_Hit";

    private Player _player;

    private void Awake() => _player = GetComponent<Player>();

    private void OnEnable()
    {
        if (_player != null) _player.ClientHitAttributed += OnHit;
    }

    private void OnDisable()
    {
        if (_player != null) _player.ClientHitAttributed -= OnHit;
    }

    private void OnHit(ulong attackerClientId)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.Play(SoundKey, transform.position, this);
    }
}
