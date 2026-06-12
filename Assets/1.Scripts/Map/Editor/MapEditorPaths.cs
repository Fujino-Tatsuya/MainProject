// 맵 시스템 에셋 경로의 단일 출처 — 폴더를 옮기면 여기만 고친다.
// 현재 구조: Assets/50.Art/MapGen (SVN 영역, 미디어만 git 제외)
//   ├─ MapObj/  : SO(Config/Catalog/ZoneDef), 오버뷰 아이콘 PNG, 프리미티브 프리팹
//   ├─ Nodes/   : 1티어/스폰 구조물 FBX
//   └─ Synty/   : 사용 중인 Synty 에셋 (프리팹/메시/머티리얼/텍스처)
public static class MapEditorPaths
{
    public const string ArtRoot = "Assets/50.Art/MapGen";
    public const string ObjRoot = ArtRoot + "/MapObj";
    public const string PrimitivesFolder = ObjRoot + "/Prefabs";

    public const string CatalogPath = ObjRoot + "/MapPrefabCatalog.asset";
    public const string ConfigPath = ObjRoot + "/MapGenConfig.asset";

    public static string ZoneDefPath(int index) => $"{ObjRoot}/ZoneDef_{index}.asset";
    public static string IconPath(string name) => $"{ObjRoot}/{name}.png";
}
