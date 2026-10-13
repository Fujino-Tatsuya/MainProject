using UnityEngine;

// 로컬라이징 L2 테스트 전용 SO — [DataTableText] 자동 키 수집. 에셋으로 만들지 않고 ScriptableObject.CreateInstance 로만 쓴다.
public sealed class LocalizedTextTestData : ScriptableObject
{
    [DataTableText] public string title = "제목";
    [DataTableText(Localize = false)] public string codeId = "ui_click";
    [DataTableText] public string emptyText = "";
    public string plain = "테이블 밖 문자열";
    public SkillTooltipText tooltip;
    public int count = 3;
}
