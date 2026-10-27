using System;
using System.Globalization;
using UnityEngine;

/// <summary>이 문자열 필드가 색 <c>#RRGGBB</c>·<c>#RRGGBBAA</c> 다 — 데이터 테이블 적용(<c>DataTableApplier.Bind</c>)이 <see cref="HexColor.TryValidate"/> 로 검증한다.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HexColorTextAttribute : Attribute
{
}

/// <summary>
/// xlsx 에서 고치는 색 문자열 <c>#RRGGBB</c>·<c>#RRGGBBAA</c>(대소문자 무관, 앞뒤 공백 허용) ↔ <see cref="Color32"/>.
/// <c>#</c> 필수 — <c>ColorUtility</c> 와 달리 색 이름(<c>red</c>)·3자리 축약은 받지 않는다(표에서 형식을 하나로).
/// </summary>
public static class HexColor
{
    public static bool TryParse(string text, out Color32 color, out string error)
    {
        color = default;
        error = null;
        string s = text?.Trim() ?? string.Empty;
        if (s.Length == 0)
        {
            error = "색이 비어 있다(#RRGGBB 또는 #RRGGBBAA).";
            return false;
        }

        if (s[0] != '#' || (s.Length != 7 && s.Length != 9))
        {
            error = $"색 '{text}' 형식이 틀렸다 — #RRGGBB 또는 #RRGGBBAA(예 #2A6E36, #2A6E36CC).";
            return false;
        }

        if (!uint.TryParse(s.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
        {
            error = $"색 '{text}' 에 16진수(0-9, A-F)가 아닌 글자가 있다.";
            return false;
        }

        if (s.Length == 7)
            value = (value << 8) | 0xFF;
        color = new Color32((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public static bool TryValidate(string text, out string error) => TryParse(text, out _, out error);

    /// <summary>알파가 FF 면 <c>#RRGGBB</c>, 아니면 <c>#RRGGBBAA</c>(대문자).</summary>
    public static string Format(Color32 color) =>
        color.a == 255
            ? $"#{color.r:X2}{color.g:X2}{color.b:X2}"
            : $"#{color.r:X2}{color.g:X2}{color.b:X2}{color.a:X2}";
}
