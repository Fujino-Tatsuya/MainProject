using UnityEngine;

/// <summary>HUD 호버가 물리 판정과 분리된 직선 미리보기 모양을 요청하는 계약.</summary>
public interface ISkillPreviewSource
{
    bool TryGetPreview(Vector3 origin, Vector3 forward, out SkillPreviewShape shape);
}
