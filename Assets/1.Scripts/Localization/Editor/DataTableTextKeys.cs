using System;
using System.Collections.Generic;
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
/// 이 문구 필드는 수치 시트에 나가지 않는다 — Strings 가 단일 원본(<see cref="DataTableTemplate.CollectLocalizedTexts"/>).
/// </summary>
public static class DataTableTextKeys
{
    /// <summary>번역 문구 칸 하나 — (수치 시트의 대상 Id, 필드경로) ↔ Strings 키. 값이 비어도 나온다(옛 수치 시트 칸 이관용).</summary>
    public readonly struct Slot
    {
        public Slot(string id, string label, string fieldPath, string key, string value)
        {
            Id = id;
            Label = label;
            FieldPath = fieldPath;
            Key = key;
            Value = value;
        }

        /// <summary>수치 시트의 대상 Id(SO 에셋 이름·프리팹 파일 이름).</summary>
        public string Id { get; }

        /// <summary>대상 표기 — SO 면 Id, 컴포넌트면 <c>프리팹 › 타입</c>.</summary>
        public string Label { get; }

        public string FieldPath { get; }
        public string Key { get; }

        /// <summary>새 Strings 행의 <c>#설명</c> — <c>대상 · 필드경로</c>.</summary>
        public string Description => $"{Label} · {FieldPath}";

        /// <summary>지금 SO·컴포넌트의 인라인 값.</summary>
        public string Value { get; }
    }

    /// <summary>대상들의 번역 문구 칸 전부(빈 값·겹치는 키 포함, 대상 순서).</summary>
    public static List<Slot> Slots(IEnumerable<(string id, Object target)> targets)
    {
        var result = new List<Slot>();
        foreach ((string id, Object target) in targets)
        {
            if (target == null)
            {
                continue;
            }

            string label = id == LocKey.OwnerId(target) ? id : $"{id} › {target.GetType().Name}";
            foreach (DataTableTemplate.Field field in DataTableTemplate.CollectLocalizedTexts(target))
            {
                string key = LocKey.ForField(target, field.Path);
                if (key != null)
                {
                    result.Add(new Slot(id, label, field.Path, key, field.Value as string ?? string.Empty));
                }
            }
        }

        return result;
    }

    public static List<StringKeyInfo> Collect(IEnumerable<(string id, Object target)> targets, List<string> warnings)
    {
        var result = new List<StringKeyInfo>();
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Slot slot in Slots(targets))
        {
            if (string.IsNullOrWhiteSpace(slot.Value))
            {
                continue;
            }

            if (claimed.TryGetValue(slot.Key, out string first))
            {
                warnings?.Add($"Strings 키 '{slot.Key}' 가 {first} 와 {slot.Label} 에서 겹친다 — {first} 의 문구만 모았다.");
                continue;
            }

            claimed[slot.Key] = slot.Label;
            result.Add(new StringKeyInfo(slot.Key, slot.Description, slot.Value));
        }

        return result;
    }
}
