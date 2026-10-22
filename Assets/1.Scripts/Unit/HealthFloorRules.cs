/// <summary>
/// 체력 하한 규칙(<see cref="Unit"/> 피해 적용) — Unity·NGO 비의존(EditMode 테스트 대상). 로비 훈련 보정(할 일 86, 명세 v0.2 §6 "체력 1 미만으로 안 내려감")이 쓴다.
/// </summary>
public static class HealthFloorRules
{
    /// <summary>
    /// 피해 뒤 체력. 하한이 0 이하면 그대로. 피해 전에 이미 하한보다 낮았으면 그 값보다 더 올리지 않는다(하한이 회복이 되지 않게).
    /// </summary>
    public static int Clamp(int previousHealth, int healthAfterDamage, int floor)
    {
        if (floor <= 0 || healthAfterDamage >= floor)
            return healthAfterDamage;

        int limit = previousHealth < floor ? previousHealth : floor;
        return healthAfterDamage > limit ? healthAfterDamage : limit;
    }
}
