using UnityEngine;

/// <summary>
/// 플레이어 아래에 있어도 <b>실루엣(벽 뒤 윤곽선) 대상에서 뺄</b> 렌더러에 붙이는 표식.
/// <see cref="PlayerSilhouetteLayers.Tag"/> 가 이 컴포넌트가 붙은 GameObject(와 그 자식)의 렌더러를 건너뛴다.
///
/// 🔴 2026-10-13: 에임 인디케이터를 데칼 → 3D 쿼드(MeshRenderer)로 바꾸자 실루엣이 쿼드 전체를
/// 몸으로 보고 <b>네모난 윤곽선</b>을 그렸다(마스크는 알파를 보지 않는다). 몸이 아닌 표시용 렌더러
/// (인디케이터·이펙트 등)는 여기에 이 표식을 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class ExcludeFromSilhouette : MonoBehaviour
{
}
