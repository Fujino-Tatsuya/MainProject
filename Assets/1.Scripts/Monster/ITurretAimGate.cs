/// <summary>
/// 고정 터렛이 <b>쏘기 전에 조준을 보여 주는</b> 구간을 관리한다.
///
/// 🔴 왜 게이트인가(2026-09-14 팀장 확정): 예고선이 뜨자마자 탄이 나가면 예고가 아니다.
/// 사거리 안에 들어오면 <b>조준선을 켜고 잠깐 플레이어를 따라간 뒤</b>, 선이 멈추는 순간 쏴야
/// 자연스럽다. 발사는 애니메이션 이벤트가 내므로(<c>attackWindup</c> 을 base 가 안 쓴다)
/// 예고 구간을 <b>공격 시작 전</b>에 둘 수밖에 없다.
///
/// 구현은 <c>TurretHeadAim</c> 이 한다. 이 인터페이스가 없으면(컴포넌트 미부착) 게이트도 없는 것과
/// 같아서 기존 동작 그대로다 — <c>MonsterBase.SeekTurret</c> 이 null 을 그렇게 다룬다.
/// </summary>
public interface ITurretAimGate
{
    /// <summary>지금 발사해도 되는가. <c>false</c> 면 아직 조준을 따라가는 중이다.</summary>
    bool IsAimReady { get; }

    /// <summary>사거리 안이고 쿨이 찼다 — 조준을 시작하거나 이어 간다. 매 틱 불린다.</summary>
    void BeginAiming();

    /// <summary>사거리 밖이거나 쏠 수 없는 상태다 — 조준을 접는다.</summary>
    void CancelAiming();
}
