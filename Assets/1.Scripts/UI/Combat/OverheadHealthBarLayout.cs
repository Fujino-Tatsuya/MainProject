using UnityEngine;

/// <summary>
/// 머리 위 체력바의 게이지 길이(0~1) 계산. MonoBehaviour 밖 순수 계산이라 EditMode 테스트로 고정한다.
/// 전체 스케일은 max(최대 체력, 체력+실드) — 실드가 붙어도 게이지가 바 밖으로 넘치지 않는다.
/// </summary>
public readonly struct OverheadHealthBarLayout
{
    /// <summary>게이지 전체 길이에 해당하는 체력량. 0 이면 모든 게이지가 0.</summary>
    public readonly float Scale;
    /// <summary>체력 게이지 끝.</summary>
    public readonly float HealthFill;
    /// <summary>실드 게이지 끝. 체력 바로 뒤에 이어 붙으므로 항상 HealthFill 이상.</summary>
    public readonly float ShieldFill;

    OverheadHealthBarLayout(float scale, float healthFill, float shieldFill)
    {
        Scale = scale;
        HealthFill = healthFill;
        ShieldFill = shieldFill;
    }

    public static OverheadHealthBarLayout Compute(int currentHealth, int maxHealth, int shield)
    {
        int health = Mathf.Max(0, currentHealth);
        int shieldAmount = Mathf.Max(0, shield);
        float scale = Mathf.Max(Mathf.Max(0, maxHealth), health + shieldAmount);
        if (scale <= 0f)
            return new OverheadHealthBarLayout(0f, 0f, 0f);

        return new OverheadHealthBarLayout(
            scale,
            Mathf.Clamp01(health / scale),
            Mathf.Clamp01((health + shieldAmount) / scale));
    }

    /// <summary>체력량(예: 잔상 값)을 이 스케일의 게이지 길이로 바꾼다.</summary>
    public float ToFill(float health) => Scale > 0f ? Mathf.Clamp01(health / Scale) : 0f;
}
