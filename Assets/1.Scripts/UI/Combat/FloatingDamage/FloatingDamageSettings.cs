using System;
using UnityEngine;

public enum DamageChannel
{
    Hp,
    Shield
}

public enum PopupKind
{
    Damage,
    Heal,
    ShieldDamage,
    Status,
    Text
}

public enum FloatingDamageDisplayFilter
{
    AllDamage,
    OwnDealtOnly,
    AllWithOwnEmphasis
}

[Serializable]
public struct FloatingPopupRequest
{
    public Unit target;
    public PopupKind kind;
    public int amount;
    public bool fromLocalPlayer;

    public FloatingPopupRequest(Unit target, PopupKind kind, int amount, bool fromLocalPlayer)
    {
        this.target = target;
        this.kind = kind;
        this.amount = amount;
        this.fromLocalPlayer = fromLocalPlayer;
    }
}

[Serializable]
public struct FloatingPopupStyle
{
    public PopupKind kind;
    public Color color;
    [Min(1f)] public float fontSize;

    public FloatingPopupStyle(PopupKind kind, Color color, float fontSize)
    {
        this.kind = kind;
        this.color = color;
        this.fontSize = fontSize;
    }
}

[CreateAssetMenu(fileName = "FloatingDamageSettings", menuName = "Combat/Floating Damage Settings")]
public sealed class FloatingDamageSettings : ScriptableObject
{
    [Header("표시 필터")]
    [SerializeField] FloatingDamageDisplayFilter displayFilter = FloatingDamageDisplayFilter.AllDamage;

    [Header("위치와 타이밍")]
    [SerializeField] Vector3 overheadWorldOffset = new Vector3(0.75f, 2.2f, 0f);
    [SerializeField, Min(0f)] float stayTimeout = 0.3f;
    [SerializeField, Min(0.01f)] float animateDuration = 0.5f;
    [SerializeField, Min(0.01f)] float fadeDuration = 0.3f;

    [Header("이동")]
    [SerializeField, Range(0f, 90f)] float scatterAngle = 35f;
    [SerializeField, Min(0f)] float initialSpeed = 2.4f;
    [SerializeField, Min(0f)] float gravity = 4f;
    [SerializeField, Min(0f)] float fadeVelocityDamping = 5f;

    [Header("강조")]
    [SerializeField, Min(1f)] float activePunchScale = 1.2f;
    [SerializeField, Min(0.01f)] float activePunchDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] float darkenMultiplier = 0.45f;
    [SerializeField, Min(1f)] float ownDamageScaleMultiplier = 1.25f;

    [Header("풀")]
    [SerializeField, Min(1)] int maxConcurrentPopups = 32;

    [Header("유형별 스타일")]
    [SerializeField] FloatingPopupStyle[] popupStyles =
    {
        new FloatingPopupStyle(PopupKind.Damage, new Color(1f, 0.82f, 0.18f, 1f), 42f),
        new FloatingPopupStyle(PopupKind.Heal, new Color(0.28f, 1f, 0.38f, 1f), 42f),
        new FloatingPopupStyle(PopupKind.ShieldDamage, new Color(0.3f, 0.8f, 1f, 1f), 38f),
        new FloatingPopupStyle(PopupKind.Status, new Color(0.85f, 0.55f, 1f, 1f), 34f),
        new FloatingPopupStyle(PopupKind.Text, Color.white, 34f)
    };

    public FloatingDamageDisplayFilter DisplayFilter => displayFilter;
    public Vector3 OverheadWorldOffset => overheadWorldOffset;
    public float StayTimeout => stayTimeout;
    public float AnimateDuration => animateDuration;
    public float FadeDuration => fadeDuration;
    public float ScatterAngle => scatterAngle;
    public float InitialSpeed => initialSpeed;
    public float Gravity => gravity;
    public float FadeVelocityDamping => fadeVelocityDamping;
    public float ActivePunchScale => activePunchScale;
    public float ActivePunchDuration => activePunchDuration;
    public float DarkenMultiplier => darkenMultiplier;
    public float OwnDamageScaleMultiplier => ownDamageScaleMultiplier;
    public int MaxConcurrentPopups => maxConcurrentPopups;

    public bool TryGetStyle(PopupKind kind, out FloatingPopupStyle style)
    {
        if (popupStyles != null)
        {
            for (int i = 0; i < popupStyles.Length; i++)
            {
                if (popupStyles[i].kind == kind)
                {
                    style = popupStyles[i];
                    return true;
                }
            }
        }

        style = default;
        return false;
    }
}
