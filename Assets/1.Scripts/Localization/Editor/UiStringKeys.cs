using System.Collections.Generic;
using System.Linq;
using UnityEditor;

/// <summary>
/// L3 UI 문구 키(<see cref="UiStrings.All"/>)를 Export 때 Strings 시트에 모은다.
/// ko = <see cref="UiString.Ko"/>(지금 화면 문구), <c>#설명</c> = 쓰는 곳, en 은 빈 칸.
/// </summary>
[InitializeOnLoad]
internal static class UiStringKeys
{
    static UiStringKeys()
    {
        StringKeyRegistry.Register(Collect);
    }

    public static IEnumerable<StringKeyInfo> Collect() =>
        UiStrings.All.Select(s => new StringKeyInfo(s.Key, s.Where, s.Ko)).ToList();
}
