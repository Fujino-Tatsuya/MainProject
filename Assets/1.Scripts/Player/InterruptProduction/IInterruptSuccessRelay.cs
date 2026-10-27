/// <summary>
/// [서버] 간파 성공을 캐릭터의 기존 RPC 로 전 피어에 퍼뜨리는 창구(할 일 118 — 전송로는 그대로 두는 어댑터, 설계 Q1-a).
/// 시전자 플레이어 아래 컴포넌트가 구현하면 디렉터 성공 단계가 성공 1회당 한 번 부른다.
/// 자기 시전인지(<see cref="InterruptProductionEvent.Skill"/>)는 구현 쪽이 가린다.
/// </summary>
public interface IInterruptSuccessRelay
{
    void ServerRelaySuccess(in InterruptProductionEvent e);
}
