/// <summary>
/// 보스 카운터 창의 **표현**. 판정은 서버가 하고(창 상태 + 정면 각도), 이 인터페이스는 각 피어에서
/// "지금 끊을 수 있다"를 플레이어에게 보여 주는 일만 한다.
///
/// 함수 하나가 아니라 인터페이스로 둔 이유: 나중에 창 **진행도**(남은 시간 게이지 등)를 표현하고
/// 싶어지면 인터페이스만 넓히면 되고, 구현체를 VFX 컴포넌트로 갈아끼워도 보스 코드는 그대로다.
/// </summary>
public interface IBossTelegraph
{
    /// <summary>창이 열렸는가. 모든 피어에서 <c>NetworkVariable</c> 변경 콜백으로 호출된다.</summary>
    void SetCounterWindow(bool open);
}
