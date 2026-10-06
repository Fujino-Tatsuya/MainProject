using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>
/// 툴팁 문자열의 자리표시자와 TMP style 태그를 검증·치환하는 순수 C# 포매터.
/// Unity 직렬화 경로와 같은 <c>field.nested[0]</c> 표기를 사용한다.
/// </summary>
public static class SkillTooltipFormatter
{
    private const string DamageColor = "#F4B860";

    public static bool TryValidate(string template, object valueSource, out string error)
    {
        return TryFormat(template, valueSource, default, false, out _, out _, out error);
    }

    public static bool TryFormat(
        string template,
        object valueSource,
        SkillTooltipDamage damage,
        bool showCalculation,
        out string text,
        out bool hasDamage,
        out string error)
    {
        template ??= string.Empty;
        hasDamage = false;
        error = null;

        if (!ValidateStyleTags(template, out error))
        {
            text = string.Empty;
            return false;
        }

        var result = new StringBuilder(template.Length + 32);
        for (int i = 0; i < template.Length; i++)
        {
            char current = template[i];
            if (current == '}')
            {
                text = string.Empty;
                error = ErrorAt(i, "여는 '{'가 없는 '}'이다.");
                return false;
            }

            if (current != '{')
            {
                result.Append(current);
                continue;
            }

            int close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                text = string.Empty;
                error = ErrorAt(i, "닫히지 않은 '{'이다.");
                return false;
            }

            string token = template.Substring(i + 1, close - i - 1);
            if (!TryFormatToken(token, i, valueSource, damage, showCalculation,
                    out string replacement, out bool tokenHasDamage, out error))
            {
                text = string.Empty;
                return false;
            }

            hasDamage |= tokenHasDamage;
            result.Append(replacement);
            i = close;
        }

        text = result.ToString();
        return true;
    }

    private static bool TryFormatToken(
        string token,
        int offset,
        object valueSource,
        SkillTooltipDamage damage,
        bool showCalculation,
        out string result,
        out bool hasDamage,
        out string error)
    {
        result = string.Empty;
        hasDamage = false;
        error = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            error = ErrorAt(offset, "빈 자리표시자다.");
            return false;
        }

        if (token == "dmg")
        {
            hasDamage = true;
            if (!damage.HasValue)
            {
                // 테이블 적용 단계에는 Player가 없어도 문법 검증은 통과해야 한다.
                result = "{dmg}";
                return true;
            }

            result = FormatDamage(damage, showCalculation);
            return true;
        }

        int colon = token.IndexOf(':');
        string path = colon < 0 ? token : token.Substring(0, colon);
        string format = colon < 0 ? null : token.Substring(colon + 1);
        if (path.Length == 0 || (colon >= 0 && format.Length == 0))
        {
            error = ErrorAt(offset, "자리표시자는 {필드} 또는 {필드:형식}이어야 한다.");
            return false;
        }

        if (!TryResolve(valueSource, path, out object value, out string resolveError))
        {
            error = ErrorAt(offset, resolveError);
            return false;
        }

        if (!TryFormatValue(value, format, out result, out string formatError))
        {
            error = ErrorAt(offset, formatError);
            return false;
        }

        return true;
    }

    private static string FormatDamage(SkillTooltipDamage damage, bool showCalculation)
    {
        string value = damage.IsRange ? $"{damage.Minimum}~{damage.Maximum}" : damage.Minimum.ToString(CultureInfo.InvariantCulture);
        string colored = $"<color={DamageColor}><sprite name=\"atk\" color={DamageColor}>{value}</color>";
        if (!showCalculation)
            return colored;

        string flat = FormatRange(damage.MinimumFlatBonus, damage.MaximumFlatBonus);
        string coefficient = FormatPercentRange(damage.MinimumCoefficient, damage.MaximumCoefficient);
        return $"{colored} = ({flat} + <sprite name=\"atk\" color={DamageColor}>{coefficient})";
    }

    private static string FormatRange(int minimum, int maximum) =>
        minimum == maximum
            ? minimum.ToString(CultureInfo.InvariantCulture)
            : $"{minimum.ToString(CultureInfo.InvariantCulture)}~{maximum.ToString(CultureInfo.InvariantCulture)}";

    private static string FormatPercentRange(float minimum, float maximum)
    {
        string min = (minimum * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        string max = (maximum * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        return MathfApproximately(minimum, maximum) ? min : min + "~" + max;
    }

    private static bool MathfApproximately(float a, float b) => Math.Abs(a - b) <= 0.00001f;

    private static bool TryFormatValue(object value, string format, out string text, out string error)
    {
        text = string.Empty;
        error = null;
        if (value == null)
        {
            error = "값이 null이다.";
            return false;
        }

        if (format == "%")
        {
            if (!TryNumber(value, out double number))
            {
                error = "% 형식은 숫자 필드에만 쓸 수 있다.";
                return false;
            }

            text = (number * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            return true;
        }

        try
        {
            if (value is IFormattable formattable)
            {
                string applied = format ?? (TryNumber(value, out _) ? "0.##" : null);
                text = formattable.ToString(applied, CultureInfo.InvariantCulture);
            }
            else
            {
                if (format != null)
                {
                    error = $"'{format}' 형식을 적용할 수 없는 필드다.";
                    return false;
                }

                text = value.ToString();
            }

            return true;
        }
        catch (FormatException)
        {
            error = $"'{format}' 형식이 올바르지 않다.";
            return false;
        }
    }

    private static bool TryNumber(object value, out double number)
    {
        try
        {
            if (value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (OverflowException)
        {
        }

        number = 0d;
        return false;
    }

    private static bool TryResolve(object root, string path, out object value, out string error)
    {
        value = root;
        error = null;
        if (root == null)
        {
            error = $"'{path}' 값을 읽을 출처가 없다.";
            return false;
        }

        string[] segments = path.Split('.');
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            string segment = segments[segmentIndex];
            if (!TryParseSegment(segment, out string name, out int? index))
            {
                error = $"필드 경로 '{path}' 형식이 올바르지 않다.";
                return false;
            }

            FieldInfo field = FindField(value.GetType(), name);
            if (field == null)
            {
                error = $"필드 '{path}'가 없다.";
                return false;
            }

            value = field.GetValue(value);
            if (index.HasValue)
            {
                if (!(value is IList list) || index.Value < 0 || index.Value >= list.Count)
                {
                    error = $"필드 '{path}'의 배열 범위를 벗어났다.";
                    return false;
                }

                value = list[index.Value];
            }

            if (value == null && segmentIndex < segments.Length - 1)
            {
                error = $"필드 '{path}'를 따라가던 중 null을 만났다.";
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSegment(string segment, out string name, out int? index)
    {
        name = segment;
        index = null;
        int open = segment.IndexOf('[');
        if (open < 0)
            return name.Length > 0;

        if (!segment.EndsWith("]", StringComparison.Ordinal) || open == 0)
            return false;

        name = segment.Substring(0, open);
        string number = segment.Substring(open + 1, segment.Length - open - 2);
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            return false;
        index = parsed;
        return true;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            FieldInfo field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }

    private static bool ValidateStyleTags(string text, out string error)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (StartsWith(text, i, "<style="))
            {
                int close = text.IndexOf('>', i + 7);
                if (close < 0)
                {
                    error = ErrorAt(i, "닫히지 않은 <style> 태그다.");
                    return false;
                }

                if (close == i + 7)
                {
                    error = ErrorAt(i, "style 이름이 비어 있다.");
                    return false;
                }

                depth++;
                i = close;
            }
            else if (StartsWith(text, i, "</style>"))
            {
                if (depth == 0)
                {
                    error = ErrorAt(i, "여는 <style>이 없는 </style>이다.");
                    return false;
                }

                depth--;
                i += "</style>".Length - 1;
            }
        }

        if (depth > 0)
        {
            error = $"문자 {text.Length + 1}: 닫히지 않은 <style> 태그가 {depth}개 있다.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool StartsWith(string text, int index, string value) =>
        index + value.Length <= text.Length && string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    private static string ErrorAt(int zeroBasedIndex, string message) => $"문자 {zeroBasedIndex + 1}: {message}";
}
