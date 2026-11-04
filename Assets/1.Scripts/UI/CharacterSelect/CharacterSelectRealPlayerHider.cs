using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 선택 미리보기 중 진짜 내 플레이어 모델을 이 화면에서만 감춘다(할 일 199, 로컬 — 다른 피어는 그대로 본다).
/// 렌더러 <c>forceRenderingOff</c> 만 쓴다(PlayerEncounterLock·HUD 숨김과 같은 방식 — enabled/SetActive 와 겹치지 않는다).
/// 이미 꺼져 있던 렌더러는 기록하지 않아 되돌릴 때 켜지 않는다. 대상이 바뀌면(캐릭터 교체 재스폰) 이전 것을 되돌리고 새 것을 감춘다.
/// </summary>
public sealed class CharacterSelectRealPlayerHider
{
    private readonly List<Renderer> _hidden = new List<Renderer>();
    private GameObject _target;

    public GameObject Target => _target;

    /// <summary>이 오브젝트 아래 렌더러를 감춘다. null 이면 되돌리기만. 같은 대상이면 새로 생긴 렌더러만 더 감춘다.</summary>
    public void Hide(GameObject target)
    {
        if (target != _target)
        {
            Restore();
            _target = target;
        }
        if (_target == null)
            return;

        foreach (Renderer r in _target.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.forceRenderingOff)
                continue;
            r.forceRenderingOff = true;
            _hidden.Add(r);
        }
    }

    public void Restore()
    {
        for (int i = 0; i < _hidden.Count; i++)
            if (_hidden[i] != null)
                _hidden[i].forceRenderingOff = false;
        _hidden.Clear();
        _target = null;
    }
}
