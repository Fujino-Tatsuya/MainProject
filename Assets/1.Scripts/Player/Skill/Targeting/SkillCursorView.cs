using UnityEngine;

/// <summary>
/// 마우스 상태별 커서 아이콘 교체 훅. PlayerSkillTargeting이 상태 변화 시 ApplyState를 호출한다.
/// 커서 텍스처 에셋이 아직 없어(기획 미정) 텍스처가 비면 no-op — 에셋이 들어오면 인스펙터 배선만으로 동작한다.
/// </summary>
public class SkillCursorView : MonoBehaviour
{
    [System.Serializable]
    private struct CursorIcon
    {
        public Texture2D texture;
        public Vector2 hotspot;
    }

    [Tooltip("각 상태의 커서 텍스처. 비워두면 해당 상태에서 커서를 바꾸지 않는다(현재 전부 비어 있어 no-op).")]
    [SerializeField] private CursorIcon defaultIcon;
    [SerializeField] private CursorIcon targetingIcon;
    [SerializeField] private CursorIcon validTargetIcon;
    [SerializeField] private CursorIcon invalidTargetIcon;
    [SerializeField] private CursorIcon outOfRangeIcon;

    public void ApplyState(SkillCursorState state)
    {
        CursorIcon icon = Resolve(state);

        // 텍스처 미배선이면 기본 시스템 커서 유지 (훅만 존재하는 현 단계의 의도된 동작)
        if (icon.texture == null)
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        Cursor.SetCursor(icon.texture, icon.hotspot, CursorMode.Auto);
    }

    // 상태별 아이콘이 비어 있으면 targeting → default 순으로 폴백한다.
    // 커서를 2개(기본/조준)만 배선해도 조준 중 상태가 바뀔 때 시스템 커서로 깜빡이지 않는다.
    private CursorIcon Resolve(SkillCursorState state)
    {
        return state switch
        {
            SkillCursorState.Targeting => FirstAssigned(targetingIcon, defaultIcon),
            SkillCursorState.ValidTarget => FirstAssigned(validTargetIcon, targetingIcon, defaultIcon),
            SkillCursorState.InvalidTarget => FirstAssigned(invalidTargetIcon, targetingIcon, defaultIcon),
            SkillCursorState.OutOfRange => FirstAssigned(outOfRangeIcon, targetingIcon, defaultIcon),
            _ => defaultIcon
        };
    }

    private static CursorIcon FirstAssigned(params CursorIcon[] icons)
    {
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i].texture != null)
                return icons[i];
        }

        return default;
    }
}
