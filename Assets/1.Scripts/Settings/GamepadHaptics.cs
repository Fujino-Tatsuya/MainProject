using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 진동 규칙(순수 함수, EditMode 테스트 대상) — 울려도 되는지·피해 배율·감쇠(포락선).
/// </summary>
public static class GamepadHapticsRules
{
    /// <summary>
    /// 울릴지: 패드 모드 + 에셋 켬 + 사용자 세기 &gt; 0 + 이 피어 오너의 사건 + 앱 포커스 있음 + 일시정지 메뉴 아님.
    /// 남의 플레이어가 맞은 것 같은 사건은 <paramref name="isLocalOwnerEvent"/> 가 false 라 안 울린다.
    /// </summary>
    public static bool ShouldPlay(bool isGamepad, bool settingsEnabled, float userScale,
        bool isLocalOwnerEvent, bool appFocused, bool paused)
    {
        return isGamepad && settingsEnabled && userScale > 0f && isLocalOwnerEvent && appFocused && !paused;
    }

    /// <summary>피해 → 배율 0~1. <paramref name="ratioForFull"/> 비율(최대 체력 대비)이면 1, 아주 작아도 <paramref name="minScale"/>.</summary>
    public static float DamageScale(int damage, int maxHp, float ratioForFull, float minScale)
    {
        if (damage <= 0) return 0f;
        if (maxHp <= 0 || ratioForFull <= 0f) return 1f;
        float t = damage / (maxHp * ratioForFull);
        return Mathf.Clamp(Mathf.Max(minScale, t), 0f, 1f);
    }

    /// <summary>진행률 0~1 → 세기 배율. 곡선에 키가 있으면 곡선, 없으면 (1 - t)^decayPower. 범위 밖은 0.</summary>
    public static float Envelope(float t, float decayPower, AnimationCurve curve)
    {
        if (t < 0f || t >= 1f) return 0f;
        if (curve != null && curve.length > 0) return Mathf.Clamp01(curve.Evaluate(t));
        if (decayPower <= 0f) return 1f;
        return Mathf.Pow(1f - t, decayPower);
    }
}

/// <summary>
/// 겹친 진동 섞기(순수, 시간은 호출자가 넘김 — unscaled). 모터마다 살아 있는 소리 중 **가장 센 값**을 쓴다(합산 안 함).
/// 같은 사건은 프리셋 <c>minInterval</c> 안에 다시 오면 무시한다.
/// </summary>
public sealed class GamepadHapticsMixer
{
    private struct Voice
    {
        public float low, high, start, duration, decayPower;
        public AnimationCurve curve;
    }

    private readonly List<Voice> _voices = new List<Voice>();
    private readonly Dictionary<int, float> _lastStart = new Dictionary<int, float>();

    public int ActiveCount => _voices.Count;

    /// <summary>소리 추가. 세기는 0~1 로 자른다. 무시되면 false.</summary>
    public bool Add(int eventKey, in GamepadHapticPreset preset, float scale, float now)
    {
        if (!preset.IsPlayable || scale <= 0f) return false;
        if (preset.minInterval > 0f && _lastStart.TryGetValue(eventKey, out float last) && now - last < preset.minInterval)
            return false;

        _lastStart[eventKey] = now;
        _voices.Add(new Voice
        {
            low = Mathf.Clamp01(preset.low * scale),
            high = Mathf.Clamp01(preset.high * scale),
            start = now,
            duration = preset.duration,
            decayPower = preset.decayPower,
            curve = preset.curve,
        });
        return true;
    }

    /// <summary>지금 모터 값. 끝난 소리는 지운다. 살아 있는 소리가 없으면 false(둘 다 0).</summary>
    public bool Evaluate(float now, out float low, out float high)
    {
        low = 0f;
        high = 0f;
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            Voice v = _voices[i];
            float t = (now - v.start) / v.duration;
            if (t >= 1f)
            {
                _voices.RemoveAt(i);
                continue;
            }
            float e = GamepadHapticsRules.Envelope(Mathf.Max(0f, t), v.decayPower, v.curve);
            low = Mathf.Max(low, v.low * e);
            high = Mathf.Max(high, v.high * e);
        }
        return _voices.Count > 0;
    }

    public void Clear()
    {
        _voices.Clear();
        _lastStart.Clear();
    }
}

/// <summary>
/// 패드 진동 단일 진입점(할 일 278 햅틱, Docs/tech/gamepad-support.md §9). 호출자는 <see cref="Play"/> 만 부른다 —
/// 세기·길이·감쇠·겹침은 여기서. 시간은 unscaled(히트스톱·슬로우 중에도 정상 종료).
/// 모터는 일시정지·포커스 잃음·씬 전환·Play 끝·기기 분리·마우스 모드 전환 때 0 으로 되돌린다.
/// <b>호출자 책임: 이 피어 오너(로컬 플레이어) 자신의 사건일 때만 부른다.</b> 패드 모드 아닐 땐 여기서 무시한다.
/// </summary>
public static class GamepadHaptics
{
    private static readonly GamepadHapticsMixer s_mixer = new GamepadHapticsMixer();
    private static Gamepad s_pad;
    private static float s_lastLow = -1f, s_lastHigh = -1f;
    private static bool s_focused = true;
    private static bool s_hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_mixer.Clear();
        s_pad = null;
        s_lastLow = s_lastHigh = -1f;
        s_focused = true;
        s_hooked = false;
    }

    /// <summary>사건 하나 울리기. <paramref name="scale"/> 은 프리셋 세기에 곱한다(피해량 등).</summary>
    public static void Play(GamepadHapticEvent e, float scale = 1f)
    {
        if (!Application.isPlaying) return;
        EnsureHooked();
        GamepadHapticsSettings settings = GamepadHapticsSettings.Load();
        float user = GameplayConfig.Haptics;
        if (!GamepadHapticsRules.ShouldPlay(InputDeviceMode.IsGamepad, settings.enabled, user,
                isLocalOwnerEvent: true, s_focused, MenuOpen))
            return;
        if (Gamepad.current == null) return;

        GamepadHapticPreset preset = settings.Get(e);
        s_mixer.Add((int)e, preset, scale * settings.masterScale * user, Time.unscaledTime);
    }

    /// <summary>플레이어 사건 — <paramref name="player"/> 가 이 피어의 로컬 플레이어일 때만 울린다.</summary>
    public static void PlayForOwner(Player player, GamepadHapticEvent e, float scale = 1f)
    {
        if (player == null || !IsLocalOwner(player)) return;
        Play(e, scale);
    }

    public static bool IsLocalOwner(Player player) => player != null && (player == Player.LocalPlayer || player.IsOwner);

    // 일시정지(게임 메뉴) 열림 — 열린 동안 진동하지 않는다.
    private static bool MenuOpen => GameMenuController.Instance != null && GameMenuController.Instance.IsOpen;

    /// <summary>피격 진동 배율(설정 SO 의 피해 비율 사용).</summary>
    public static float DamageScale(int damage, int maxHp)
    {
        GamepadHapticsSettings s = GamepadHapticsSettings.Load();
        return GamepadHapticsRules.DamageScale(damage, maxHp, s.damageRatioForFull, s.damageMinScale);
    }

    /// <summary>모든 소리를 끊고 모터 0.</summary>
    public static void ResetHaptics()
    {
        s_mixer.Clear();
        ApplyMotors(0f, 0f, force: true);
    }

    /// <summary><see cref="InputDeviceModeDriver"/> 가 매 프레임 부른다.</summary>
    internal static void Tick()
    {
        Gamepad current = Gamepad.current;
        if (current != s_pad)
        {
            // 패드가 바뀌면 이전 패드부터 멈춘다.
            if (s_pad != null && s_pad.added) s_pad.ResetHaptics();
            s_pad = current;
            s_lastLow = s_lastHigh = -1f;
            s_mixer.Clear();
        }
        if (s_pad == null) return;

        if (!InputDeviceMode.IsGamepad || !s_focused || MenuOpen)
        {
            if (s_mixer.ActiveCount > 0 || s_lastLow != 0f || s_lastHigh != 0f) ResetHaptics();
            return;
        }

        s_mixer.Evaluate(Time.unscaledTime, out float low, out float high);
        ApplyMotors(low, high, force: false);
    }

    internal static void SetFocused(bool focused)
    {
        s_focused = focused;
        if (!focused) ResetHaptics();
    }

    private static void ApplyMotors(float low, float high, bool force)
    {
        if (s_pad == null || !s_pad.added) return;
        if (!force && Mathf.Approximately(low, s_lastLow) && Mathf.Approximately(high, s_lastHigh)) return;
        s_lastLow = low;
        s_lastHigh = high;
        if (low <= 0f && high <= 0f) s_pad.ResetHaptics();
        else s_pad.SetMotorSpeeds(low, high);
    }

    private static void EnsureHooked()
    {
        if (s_hooked) return;
        s_hooked = true;
        SceneManager.activeSceneChanged += (_, __) => ResetHaptics();
        Application.quitting += ResetHaptics;
        InputSystem.onDeviceChange += (device, change) =>
        {
            if (device != s_pad) return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                s_mixer.Clear();
                s_pad = null;
                s_lastLow = s_lastHigh = -1f;
            }
        };
        InputDeviceMode.Changed += kind => { if (kind != InputDeviceKind.Gamepad) ResetHaptics(); };
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += state =>
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) ResetHaptics();
        };
#endif
    }
}
