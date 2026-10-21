using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 적 외곽선이 마스크에 그릴 렌더러 고르기 — 메시를 그리는 모델 렌더러만.
/// 연출용(장판·<see cref="NoHitFlash"/> 표식, HitFlash 와 같은 제외 규칙)·반투명(메인 머티리얼이 투명 큐)은 뺀다.
///
/// 🔴 렌더러·머티리얼은 <b>읽기만</b> 한다(sharedMaterial 조회뿐). 몬스터 쪽 연출(MonsterAttackTell·DissolveDeath·붕괴 조각·충전기)이
/// 머티리얼 배열을 스스로 바꾸고 복원하기 때문이다. 마스크는 렌더 피처가 같은 렌더러를 덮어쓰기 머티리얼로 한 번 더 그려 만든다.
/// </summary>
public static class EnemyOutlineRenderers
{
    /// <summary><paramref name="root"/> 아래 외곽선 대상 렌더러를 <paramref name="into"/> 에 덧붙인다(비활성 포함 — 켜짐은 매 프레임 따로 본다).</summary>
    public static void Collect(Transform root, List<Renderer> into, List<Renderer> scratch)
    {
        if (root == null || into == null || scratch == null)
            return;

        scratch.Clear();
        root.GetComponentsInChildren(true, scratch);
        for (int i = 0; i < scratch.Count; i++)
            if (IsOutlineSource(scratch[i]))
                into.Add(scratch[i]);
        scratch.Clear();
    }

    public static bool IsOutlineSource(Renderer r)
    {
        if (r == null)
            return false;
        if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
            return false;

        Material main = r.sharedMaterial;
        if (main == null || main.renderQueue > (int)RenderQueue.GeometryLast)
            return false;
        if (r.GetComponentInParent<AoeTelegraph>() != null || r.GetComponentInParent<NoHitFlash>() != null)
            return false;

        return GetMesh(r) != null;
    }

    /// <summary>
    /// 이번 프레임 그려도 되는가 — 원래 렌더러가 꺼지면(23호 체공 숨김·붕괴 사망의 <c>enabled=false</c>, <c>forceRenderingOff</c>)
    /// 외곽선도 따라 꺼진다.
    /// </summary>
    public static bool IsDrawable(Renderer r) =>
        r != null && r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy;

    public static int SubMeshCount(Renderer r)
    {
        UnityEngine.Mesh mesh = GetMesh(r);
        return mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 0;
    }

    // 프로젝트에 Mesh 네임스페이스가 있어 타입은 UnityEngine.Mesh 로 적는다.
    static UnityEngine.Mesh GetMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer skin)
            return skin.sharedMesh;

        MeshFilter filter = r.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }
}
