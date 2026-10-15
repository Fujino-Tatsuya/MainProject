using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 드롭다운. 펼칠 때 만드는 "바깥 클릭 차단막"의 레이캐스터를 <see cref="MonitorAwareRaycaster"/> 로 바꿔
/// 타이틀 모니터 RT 에서도 좌표가 맞게 한다(목록 쪽은 템플릿에 미리 붙여 둔다 — 드롭다운이 있으면 그걸 쓴다).
/// </summary>
public sealed class SettingsDropdown : TMP_Dropdown
{
    // 🔴 목록은 별도 캔버스라 **자기 레이어로 컬링**된다. 타이틀 모니터 UI 카메라는 UI 레이어만 찍으므로
    //    목록 레이어가 Default 면 클릭(차단막은 루트 레이어)만 먹고 목록은 안 보였다(팀장 10-12 Play).
    //    드롭다운 자신의 레이어로 맞춘다.
    protected override GameObject CreateDropdownList(GameObject template)
    {
        GameObject list = base.CreateDropdownList(template);
        SetLayerRecursively(list.transform, gameObject.layer);
        return list;
    }

    static void SetLayerRecursively(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i), layer);
    }

    protected override GameObject CreateBlocker(Canvas rootCanvas)
    {
        GameObject blocker = base.CreateBlocker(rootCanvas);
        GraphicRaycaster raycaster = blocker.GetComponent<GraphicRaycaster>();
        if (raycaster != null && !(raycaster is MonitorAwareRaycaster))
        {
            // 같은 프레임에 두 레이캐스터가 겹치지 않게 즉시 지운다(Destroy 는 프레임 끝).
            DestroyImmediate(raycaster);
            blocker.AddComponent<MonitorAwareRaycaster>();
        }
        return blocker;
    }
}
