using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 설정창 글꼴 에셋 — 아트 원본 서체 <b>서울알림체 Heavy</b>(SeoulAlrimTTF-Heavy)로 TMP 에셋을 만든다(PLAN-settings-art A4).
/// 원본: 서울시 공식 배포(www.seoul.go.kr/seoul/font.do, 10-20 받음). 이용 조건 = 무료·자유 사용, 판매·유상양도·무단배포 금지,
/// 공공누리 제1유형(출처 표시) — 크레딧에 "서울알림체 © 서울특별시" 표기 필요.
///
/// 🔴 <b>정적(Static)</b> 에셋으로 만든다 — 동적이면 Play 때마다 아틀라스가 바뀌어 SVN 에 쓸데없는 변경이 생긴다.
///    설정창 문구(ko·en) + ASCII 를 미리 굽고, 여기 없는 글자(시트에서 바뀐 문구 등)는 NotoSansKR 폴백이 그린다.
/// 다시 실행하면 기존 에셋에 빠진 글자만 더한다(멱등 — guid·기존 글리프 유지).
/// </summary>
public static class SettingsFontAssetAuthoring
{
    const string FontPath = "Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy.ttf";
    const string FallbackPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";
    const int SamplingSize = 72;
    const int Padding = 8;
    const int AtlasSize = 2048;

    [MenuItem("Tools/UI/Settings/설정창 글꼴 에셋 만들기 (서울알림체)")]
    static void Create()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null) { Debug.LogError($"[SettingsFont] 서체 파일이 없다: {FontPath}"); return; }

        string path = SettingsArtSkin.FontAssetPath;
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (asset == null)
        {
            asset = TMP_FontAsset.CreateFontAsset(font, SamplingSize, Padding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            if (asset == null) { Debug.LogError("[SettingsFont] TMP 에셋 생성 실패."); return; }
            asset.name = "SeoulAlrimTTF-Heavy SDF";
            AssetDatabase.CreateAsset(asset, path);
            AttachSubAssets(asset);
        }
        else
        {
            // 🔴 이미 있으면 지우지 않고 글자만 더한다 — guid·기존 글리프 유지(프리팹 참조가 안 끊기게).
            // 정적 에셋은 원본 서체 참조를 비워 둘 수 있다 — 글자를 더하려면 다시 걸어야 한다(setter 가 internal 이라 직렬화로).
            var so = new SerializedObject(asset);
            so.FindProperty("m_SourceFontFile").objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();
            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        }

        string chars = CharacterSet();
        asset.TryAddCharacters(chars, out string missing);
        AttachSubAssets(asset);

        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);
        asset.fallbackFontAssetTable = fallback != null ? new List<TMP_FontAsset> { fallback } : new List<TMP_FontAsset>();

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path);

        int missingCount = string.IsNullOrEmpty(missing) ? 0 : missing.Length;
        Debug.Log($"[SettingsFont] {path} 저장 · 글자 {chars.Length}개 요청 · 서체에 없는 글자 {missingCount}개" +
                  (missingCount > 0 ? $"(폴백 NotoSansKR 이 그림): {missing}" : "") +
                  $" · 아틀라스 {asset.atlasTextures.Length}장 · 폴백 {(fallback != null ? "NotoSansKR" : "없음 ⚠️")}");
    }

    // 동적 생성 중 만들어진 아틀라스·머티리얼을 에셋 안에 넣는다(안 넣으면 저장 후 텍스처가 사라진다).
    static void AttachSubAssets(TMP_FontAsset asset)
    {
        for (int i = 0; i < asset.atlasTextures.Length; i++)
        {
            Texture2D tex = asset.atlasTextures[i];
            if (tex == null || AssetDatabase.Contains(tex)) continue;
            tex.name = $"{asset.name} Atlas{(i == 0 ? "" : " " + i)}";
            AssetDatabase.AddObjectToAsset(tex, asset);
        }
        if (asset.material != null && !AssetDatabase.Contains(asset.material))
        {
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
        }
    }

    /// <summary>ASCII + UI 문구 전부(<see cref="UiStrings.All"/> ko — 설정창·일시정지 메뉴 포함) + 설정창 영어 + 언어 이름 + 기호.</summary>
    internal static string CharacterSet()
    {
        var set = new SortedSet<char>();
        for (char c = ' '; c <= '~'; c++) set.Add(c);
        void AddAll(string s) { if (s == null) return; foreach (char c in s) if (!char.IsControl(c)) set.Add(c); }
        foreach (UiString s in UiStrings.All) AddAll(s.Ko);   // SettingsUiStrings 포함(10-20 일시정지 메뉴 추가로 확장)
        foreach (string en in SettingsUiStrings.AuthoredEnglish.Values) AddAll(en);
        AddAll(LocText.NativeName(GameLanguage.Ko));
        AddAll(LocText.NativeName(GameLanguage.En));
        AddAll("×·…–—%");
        var sb = new StringBuilder(set.Count);
        foreach (char c in set) sb.Append(c);
        return sb.ToString();
    }
}
