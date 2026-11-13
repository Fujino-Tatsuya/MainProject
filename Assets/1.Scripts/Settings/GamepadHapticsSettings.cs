using System;
using UnityEngine;

/// <summary>패드 진동 사건(할 일 278 햅틱, Docs/tech/gamepad-support.md §9).</summary>
public enum GamepadHapticEvent
{
    DamageTaken,
    InterruptSuccessNormal,
    InterruptSuccessDash,
    InterruptSuccessSuppress,
    InterruptFail,
    Dash,
    BasicAttackHit,
    SkillCast,
    UltimateCast,
    Death,
    BossKill,
    BossIntroImpact,
}

/// <summary>
/// 진동 한 종류 — 저주파(왼쪽, 묵직) · 고주파(오른쪽, 날카로움) 모터 세기 0~1, 길이(초, unscaled), 감쇠.
/// 감쇠: <see cref="curve"/> 에 키가 있으면 그 곡선(가로 0~1 = 진행률, 세로 = 배율), 없으면 (1 - 진행률)^<see cref="decayPower"/>.
/// </summary>
[Serializable]
public struct GamepadHapticPreset
{
    [Tooltip("이 사건 진동 켜기.")]
    public bool enabled;
    [Range(0f, 1f), Tooltip("저주파(왼쪽) 모터 세기 — 묵직한 울림.")]
    public float low;
    [Range(0f, 1f), Tooltip("고주파(오른쪽) 모터 세기 — 짧고 날카로운 떨림.")]
    public float high;
    [Min(0f), Tooltip("길이(초, 실제 시간 — 히트스톱·슬로우와 무관).")]
    public float duration;
    [Min(0f), Tooltip("감쇠 지수 — 0 = 끝까지 일정, 1 = 직선으로 줄어듦, 2 = 앞에서 세고 빨리 빠짐. 곡선에 키가 있으면 곡선이 이긴다.")]
    public float decayPower;
    [Min(0f), Tooltip("같은 사건 최소 간격(초). 이 안에 다시 오면 무시 — 다단 히트가 과하지 않게.")]
    public float minInterval;
    [Tooltip("선택 — 진행률(0~1) → 세기 배율 곡선. 비우면 감쇠 지수를 쓴다.")]
    public AnimationCurve curve;

    public GamepadHapticPreset(float low, float high, float duration, float decayPower = 1f, float minInterval = 0f)
    {
        enabled = true;
        this.low = low;
        this.high = high;
        this.duration = duration;
        this.decayPower = decayPower;
        this.minInterval = minInterval;
        curve = null;
    }

    public bool IsPlayable => enabled && duration > 0f && (low > 0f || high > 0f);
}

/// <summary>
/// 패드 진동 수치(할 일 278 햅틱) — 사건별 프리셋 + 전체 배율 + 켜기 스위치. 위치 <c>Assets/Resources/GamepadHaptics/GamepadHapticsSettings.asset</c>.
/// 사용자 설정(설정창 게임플레이 '패드 진동' 0~100%)은 <see cref="GameplayConfig.Haptics"/> 로 따로 곱한다(이 에셋은 안 고친다).
/// 기본값은 보수적으로(시끄럽지 않게) 잡았다.
/// </summary>
[CreateAssetMenu(menuName = "Settings/Gamepad Haptics Settings", fileName = "GamepadHapticsSettings")]
public sealed class GamepadHapticsSettings : ScriptableObject
{
    public const string ResourcePath = "GamepadHaptics/GamepadHapticsSettings";

    [Tooltip("전체 켜기. 끄면 어떤 사건도 울리지 않는다.")]
    public bool enabled = true;
    [Range(0f, 2f), Tooltip("전체 세기 배율(모든 사건에 곱한 뒤 0~1 로 자른다).")]
    public float masterScale = 1f;
    [Range(0.01f, 1f), Tooltip("피격 진동이 프리셋 최대가 되는 피해 비율(최대 체력 대비). 0.25 = 최대 체력 25% 한 방이면 최대.")]
    public float damageRatioForFull = 0.25f;
    [Range(0f, 1f), Tooltip("피격 진동 최소 배율 — 아주 작은 피해도 이만큼은 울린다.")]
    public float damageMinScale = 0.3f;

    [Header("사건별 프리셋")]
    public GamepadHapticPreset damageTaken = new GamepadHapticPreset(0.25f, 0.35f, 0.15f, 1f, 0.1f);
    public GamepadHapticPreset interruptSuccessNormal = new GamepadHapticPreset(0.45f, 0.6f, 0.25f, 1.5f);
    public GamepadHapticPreset interruptSuccessDash = new GamepadHapticPreset(0.6f, 0.75f, 0.3f, 1.5f);
    public GamepadHapticPreset interruptSuccessSuppress = new GamepadHapticPreset(0.85f, 1f, 0.45f, 1.5f);
    public GamepadHapticPreset interruptFail = new GamepadHapticPreset(0.15f, 0.3f, 0.08f, 1f);
    public GamepadHapticPreset dash = new GamepadHapticPreset(0f, 0.12f, 0.06f, 1f);
    public GamepadHapticPreset basicAttackHit = new GamepadHapticPreset(0.05f, 0.18f, 0.05f, 1f, 0.08f);
    public GamepadHapticPreset skillCast = new GamepadHapticPreset(0.12f, 0.2f, 0.1f, 1f);
    public GamepadHapticPreset ultimateCast = new GamepadHapticPreset(0.3f, 0.4f, 0.25f, 1f);
    public GamepadHapticPreset death = new GamepadHapticPreset(0.5f, 0.3f, 0.8f, 1.5f);
    public GamepadHapticPreset bossKill = new GamepadHapticPreset(0.8f, 0.9f, 0.6f, 1.5f);
    public GamepadHapticPreset bossIntroImpact = new GamepadHapticPreset(0.5f, 0.4f, 0.4f, 1.5f);

    public GamepadHapticPreset Get(GamepadHapticEvent e)
    {
        switch (e)
        {
            case GamepadHapticEvent.DamageTaken: return damageTaken;
            case GamepadHapticEvent.InterruptSuccessNormal: return interruptSuccessNormal;
            case GamepadHapticEvent.InterruptSuccessDash: return interruptSuccessDash;
            case GamepadHapticEvent.InterruptSuccessSuppress: return interruptSuccessSuppress;
            case GamepadHapticEvent.InterruptFail: return interruptFail;
            case GamepadHapticEvent.Dash: return dash;
            case GamepadHapticEvent.BasicAttackHit: return basicAttackHit;
            case GamepadHapticEvent.SkillCast: return skillCast;
            case GamepadHapticEvent.UltimateCast: return ultimateCast;
            case GamepadHapticEvent.Death: return death;
            case GamepadHapticEvent.BossKill: return bossKill;
            case GamepadHapticEvent.BossIntroImpact: return bossIntroImpact;
            default: return default;
        }
    }

    private static GamepadHapticsSettings s_cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_cached = null;

    public static GamepadHapticsSettings Load()
    {
        if (s_cached != null) return s_cached;
        s_cached = Resources.Load<GamepadHapticsSettings>(ResourcePath);
        if (s_cached == null)
        {
            Debug.LogWarning($"[GamepadHaptics] Resources/{ResourcePath} 가 없어 코드 기본값으로 돈다.");
            s_cached = CreateInstance<GamepadHapticsSettings>();
        }
        return s_cached;
    }
}
