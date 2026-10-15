using System.Collections.Generic;
using System.Linq;
using UnityEditor;

/// <summary>
/// L4 오류 코드(<see cref="ErrorCatalog.All"/>)를 Export 때 ErrorCodes 시트에 모은다.
/// ko = <see cref="ErrorCode.Ko"/>(지금 화면 문구), <c>#분류 | #원인(개발자용) | #조치(QA·CS용)</c> = 코드에 적힌 메모, en 은 빈 칸.
/// 시트에만 있고 목록에 없는 행은 Export 가 경고한다(<see cref="StringsSheet.UnregisteredKeys"/>).
/// </summary>
[InitializeOnLoad]
internal static class ErrorCodeKeys
{
    static ErrorCodeKeys()
    {
        StringKeyRegistry.RegisterErrorCodes(Collect);
    }

    public static IEnumerable<StringKeyInfo> Collect() =>
        ErrorCatalog.All.Select(e => new StringKeyInfo(e.Code, new[] { e.Category, e.Cause, e.Action }, e.Ko)).ToList();
}
