using System;
using System.Collections.Generic;
using System.Reflection;
using Object = UnityEngine.Object;

/// <summary>
/// Export 때 <c>[DataTableText]</c> 문자열을 Strings 시트 키로 모은다(로컬라이징 L2).
/// 대상 = 데이터 테이블 대상(<see cref="DataTableTemplate.CollectTargets"/> — <c>[DataTableSheet]</c> SO·프리팹 컴포넌트).
/// <list type="bullet">
/// <item>키 = <see cref="LocKey.ForField(Object, string)"/> — <c>주인ID/필드경로</c>. 런타임 getter 와 같은 함수.</item>
/// <item>ko = 지금 SO 값, #설명 = <c>대상 · 필드경로</c>, en 은 빈 칸(<see cref="StringsSheet.BuildSheet"/>).</item>
/// <item>빼는 것 — <c>[DataTableText(Localize = false)]</c>(코드 ID·기획 메모), 빈 값(ko 빈 칸은 시트 오류라서).</item>
/// <item>같은 키가 두 대상에서 나오면(같은 컴포넌트 타입이 두 프리팹에) 처음 것만 쓰고 경고.</item>
/// </list>
/// 시트에 이미 있는 키는 BuildSheet 가 건드리지 않는다 — 기획이 고친 ko·en 이 Export 로 덮이지 않는다.
/// </summary>
public static class DataTableTextKeys
{
    public static List<StringKeyInfo> Collect(IEnumerable<(string id, Object target)> targets, List<string> warnings)
    {
        var result = new List<StringKeyInfo>();
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string id, Object target) in targets)
        {
            if (target == null)
            {
                continue;
            }

            Type type = target.GetType();
            string label = id == LocKey.OwnerId(target) ? id : $"{id} › {type.Name}";
            foreach (DataTableTemplate.Field field in DataTableTemplate.CollectFields(target))
            {
                if (!(field.Value is string ko) || string.IsNullOrWhiteSpace(ko))
                {
                    continue;
                }

                FieldInfo info = DataTableFields.Resolve(type, DataTableApplier.ToPropertyPath(field.Path));
                if (!DataTableFields.IsLocalizedText(info))
                {
                    continue;
                }

                string key = LocKey.ForField(target, field.Path);
                if (key == null)
                {
                    continue;
                }

                if (claimed.TryGetValue(key, out string first))
                {
                    warnings?.Add($"Strings 키 '{key}' 가 {first} 와 {label} 에서 겹친다 — {first} 의 문구만 모았다.");
                    continue;
                }

                claimed[key] = label;
                result.Add(new StringKeyInfo(key, $"{label} · {field.Path}", ko));
            }
        }

        return result;
    }
}
