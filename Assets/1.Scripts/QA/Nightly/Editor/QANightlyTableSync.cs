#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 할 일 140-3 — 야간 QA 테이블 싱크 사전 검사(순수). 테이블 Play 는 준비 오류가 있으면 진입을 취소한다(<c>DataSourcePlayMode</c>) —
/// 그대로 돌리면 판마다 Play 진입 상한(180초)을 기다리다 <c>play_start</c> 오류로 판을 버린다(N108 판 3~5: xlsx 에 Boss.parryRetreatTime 행, 코드엔 필드 없음).
/// 그래서 배치 시작·Resume·play_start 오류 뒤에 같은 준비(<c>DataTableSource.Load</c>)를 미리 돌려, 오류면 Play 없이 배치를 멈춘다.
/// 배치 머리(batch.txt)에는 Assets/50.Art SVN 리비전·origin/development 포함 여부(로컬 ref 기준, fetch 안 함)도 남긴다.
/// </summary>
public static class QANightlyTableSync
{
    /// <summary>batch.txt — 사전 검사 결과(<see cref="Ok"/>·<see cref="NoXlsx"/> 또는 멈춤 문구).</summary>
    public const string KeyTableSync = "table_sync";
    /// <summary>batch.txt — Assets/50.Art 작업 사본 SVN 리비전(실패 = na).</summary>
    public const string KeyArtRev = "art_svn_rev";
    /// <summary>batch.txt — 로컬 ref origin/development 짧은 커밋(실패 = na).</summary>
    public const string KeyDevRef = "origin_dev";
    /// <summary>batch.txt — HEAD 가 origin/development 를 포함하나(yes·no·na).</summary>
    public const string KeyDevMerged = "origin_dev_merged";

    public const string Ok = "ok";
    public const string NoXlsx = "no_xlsx";
    public const string Na = "na";
    public const string Yes = "yes";
    public const string No = "no";

    /// <summary>멈춤 문구에 이름을 보이는 필드 수 상한(나머지는 "… 외 N개").</summary>
    public const int ListLimit = 5;

    private static readonly Regex FieldQuoted = new Regex(@"필드 '(?<f>[^']+)' 가 없다", RegexOptions.CultureInvariant);
    private static readonly Regex FieldArray = new Regex(@"^\w+\.(?<f>\S+) — 필드가 없거나", RegexOptions.CultureInvariant);

    /// <summary>
    /// 테이블 오류 한 줄 → 필드 없음이면 <c>시트.필드</c>(예 <c>Boss.parryRetreatTime</c>), 아니면 null.
    /// <paramref name="location"/> = <c>GameData.xlsx › Boss!C5</c> 형식(시트 이름은 › 와 ! 사이), <paramref name="message"/> = DataTableApplier 오류 문구.
    /// </summary>
    public static string MissingField(string location, string message)
    {
        if (string.IsNullOrEmpty(message))
            return null;
        Match m = FieldQuoted.Match(message);
        if (!m.Success)
            m = FieldArray.Match(message);
        if (!m.Success)
            return null;
        string sheet = SheetOf(location);
        string field = m.Groups["f"].Value;
        return sheet.Length > 0 ? sheet + "." + field : field;
    }

    /// <summary><c>파일 › 시트!칸</c> → 시트. 형식이 아니면 빈 값.</summary>
    public static string SheetOf(string location)
    {
        string s = location ?? "";
        int a = s.IndexOf('›');
        if (a < 0)
            return "";
        int b = s.IndexOf('!', a + 1);
        string sheet = b < 0 ? s.Substring(a + 1) : s.Substring(a + 1, b - a - 1);
        return sheet.Trim();
    }

    /// <summary>
    /// 오류 목록(위치·문구) → 필드 없음 목록(중복 제거, 처음 나온 순서) + 그 밖의 오류 수·첫 오류.
    /// </summary>
    public static List<string> Classify(IEnumerable<KeyValuePair<string, string>> errors, out int otherCount, out string firstOther)
    {
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        otherCount = 0;
        firstOther = null;
        if (errors == null)
            return missing;
        foreach (KeyValuePair<string, string> e in errors)
        {
            string f = MissingField(e.Key, e.Value);
            if (f == null)
            {
                otherCount++;
                if (firstOther == null)
                    firstOther = string.IsNullOrEmpty(e.Key) ? (e.Value ?? "") : $"{e.Key}: {e.Value}";
                continue;
            }
            if (seen.Add(f))
                missing.Add(f);
        }
        return missing;
    }

    /// <summary>필드 없음 목록 → <c>Boss.parryRetreatTime, Boss.x, … 외 3개</c>(상한 <see cref="ListLimit"/>).</summary>
    public static string FormatList(IReadOnlyList<string> fields)
    {
        if (fields == null || fields.Count == 0)
            return "";
        var sb = new StringBuilder();
        int shown = Math.Min(fields.Count, ListLimit);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(fields[i]);
        }
        if (fields.Count > shown)
            sb.Append(", … 외 ").Append((fields.Count - shown).ToString(CultureInfo.InvariantCulture)).Append("개");
        return sb.ToString();
    }

    /// <summary>
    /// 멈춤 문구(loop.log·batch.txt <see cref="KeyTableSync"/>·nightly-summary.md). 오류가 없으면 null.
    /// 필드 없음 = <c>테이블 싱크 불일치(필드 없음 N개: …) — development 병합 또는 SVN 업데이트 필요</c>, 그 밖의 오류는 뒤에 덧붙인다.
    /// </summary>
    public static string StopMessage(IReadOnlyList<string> missing, int otherCount, string firstOther)
    {
        int n = missing?.Count ?? 0;
        string other = otherCount > 0
            ? $"그 밖의 테이블 오류 {otherCount.ToString(CultureInfo.InvariantCulture)}개(첫 오류: {Flat(firstOther)}) — Tools/Data/Verify 로 확인"
            : "";
        if (n > 0)
            return $"테이블 싱크 불일치(필드 없음 {n.ToString(CultureInfo.InvariantCulture)}개: {FormatList(missing)}) — development 병합 또는 SVN 업데이트 필요" +
                   (other.Length > 0 ? " · " + other : "");
        if (otherCount > 0)
            return $"테이블 준비 오류 — {other}, development 병합 또는 SVN 업데이트 필요";
        return null;
    }

    /// <summary>batch.txt <see cref="KeyTableSync"/> 값이 멈춤(오류) 문구인가.</summary>
    public static bool IsStop(string tableSync)
    {
        string s = (tableSync ?? "").Trim();
        return s.Length > 0 && s != Ok && s != NoXlsx;
    }

    /// <summary><c>git merge-base --is-ancestor origin/development HEAD</c> 종료 코드 → yes(0)·no(1)·na(실행 실패·그 밖).</summary>
    public static string DevMerged(int? exitCode)
    {
        if (exitCode == 0)
            return Yes;
        if (exitCode == 1)
            return No;
        return Na;
    }

    /// <summary>리비전·커밋 출력 정리 — 비었거나 실패면 na.</summary>
    public static string OrNa(string output)
    {
        string s = (output ?? "").Trim();
        return s.Length == 0 || s == "unknown" ? Na : s;
    }

    /// <summary>
    /// 요약·로그용 한 줄 — <c>yes — origin/development f11f5d84 포함(로컬 ref, fetch 안 함)</c>.
    /// no 면 development 병합 안내, na 면 확인 실패.
    /// </summary>
    public static string DevLine(string devRef, string merged)
    {
        string r = OrNa(devRef);
        switch ((merged ?? "").Trim())
        {
            case Yes: return $"yes — origin/development {r} 포함(로컬 ref, fetch 안 함)";
            case No: return $"no — origin/development {r} 미포함(로컬 ref, fetch 안 함) — development 병합 필요";
            default: return $"na — 확인 실패(origin/development {r})";
        }
    }

    private static string Flat(string s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
}
#endif
