/// <summary>
/// 간파 대상의 판정 상태(할 일 191) — <see cref="InterruptManager"/> 가 매 프레임 샘플한다. 구현 안 한 대상은 늘 간파 가능으로 본다.
/// </summary>
public interface IInterruptJudgeTarget
{
    /// <summary>
    /// [모든 피어] 간파 불가 상태(취약·제압). 서버 = 판정 원본, 클라 = 복제값 근사(23호 제압은 복제값이 없어 취약만 — 제압 중엔 창이 안 열려 판정에는 영향 없다).
    /// </summary>
    bool InterruptBlockedByState { get; }
}
