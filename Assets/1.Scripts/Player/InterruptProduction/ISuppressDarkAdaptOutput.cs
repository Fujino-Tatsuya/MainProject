/// <summary>
/// 적중 라이트 연출 암순응 가(화면 패스) 출력(할 일 194) — 렌더 출력은 <see cref="InterruptDarkAdaptation"/>, 테스트는 가짜.
/// 처음엔 제압 암순응 컷(할 일 192 Q7, 11-04 Legacy)용으로 만들었고 이름은 그대로 둔다. 모든 피어 같은 값을 붙잡는다.
/// </summary>
public interface ISuppressDarkAdaptOutput
{
    /// <summary>이 어둡힘을 그대로 붙잡는다(곡선 없음). 컷이 바뀔 때마다 부른다.</summary>
    void ShowDarkAdaptCut(float darkness);

    /// <summary>즉시 0(컷 끝·강제 리셋).</summary>
    void ClearDarkAdapt();
}
