using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 공용 처치 연출 색 = 팔라딘 기준으로 굽기(할 일 271, 11-12 사용자 지시). 공용 설정의 팔레트 15칸에 팔라딘 프로필이 지금 계산하는 최종 색
/// (<see cref="BossVictoryPaletteCalc.Compute(BossVictoryGlitchSettings, BossVictoryProfile)"/>, 직접 지정 포함)을 써 넣는다 — 공용 amberColor = 팔라딘 기준 색.
/// 그 뒤 팔라딘은 '기준 색 = 공용 호박색' 이라 결과가 그대로, 다른 캐릭터는 같은 관계로 따라간다(클램프·무채색 경계에서만 조금 달라질 수 있음 — 로그에 최대 채널 오차).
/// 메뉴 또는 batchmode <c>-executeMethod BossVictoryPaletteBake271.Bake</c>. 다시 실행해도 같은 결과(팔라딘 = 공용이면 바뀌는 것 없음).
/// </summary>
public static class BossVictoryPaletteBake271
{
    const string SettingsPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    static readonly TutorialCharacter[] Characters = { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };

    [MenuItem("Tools/Boss Victory/공용 팔레트 = 팔라딘 기준으로 굽기 (271)")]
    public static void Bake()
    {
        var s = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        if (s == null)
        {
            Debug.LogError($"[BossVictory271] 설정 에셋이 없다 — {SettingsPath}");
            return;
        }
        BossVictoryProfile paladin = s.ProfileFor(TutorialCharacter.Paladin);
        if (!s.HasOwnProfile(TutorialCharacter.Paladin))
        {
            Debug.LogError("[BossVictory271] 표에 팔라딘 프로필이 없다 — 굽지 않는다.");
            return;
        }

        var before = new BossVictoryPalette[Characters.Length];
        for (int i = 0; i < Characters.Length; i++) before[i] = BossVictoryPaletteCalc.Compute(s, s.ProfileFor(Characters[i]));
        BossVictoryPalette p = before[0];

        Undo.RecordObject(s, "공용 팔레트 = 팔라딘 기준으로 굽기 (271)");
        s.amberColor = p[BossVictoryPaletteSlot.Amber];
        s.bossNoiseBrightColor = p[BossVictoryPaletteSlot.NoiseBright];
        s.bossNoiseDarkColor = p[BossVictoryPaletteSlot.NoiseDark];
        s.rectifyColor = p[BossVictoryPaletteSlot.RectifyOutline];
        BossVictoryGlitchSettings.RectifyCircle[] circles = s.rectifyCircles;
        if (circles == null || circles.Length < 3)
        {
            BossVictoryGlitchSettings.RectifyCircle[] fixedCircles = BossVictoryGlitchSettings.DefaultRectifyCircles();
            for (int i = 0; circles != null && i < circles.Length; i++) fixedCircles[i] = circles[i];
            circles = fixedCircles;
        }
        for (int i = 0; i < 3; i++)
        {
            circles[i].leftColor = p[BossVictoryPaletteSlot.Circle1Left + 2 * i];
            circles[i].rightColor = p[BossVictoryPaletteSlot.Circle1Right + 2 * i];
        }
        s.rectifyCircles = circles;
        s.rectifyCircleGrayColor = p[BossVictoryPaletteSlot.CircleGray];
        s.rectifyOrangeLeft = p[BossVictoryPaletteSlot.OrangeLeft];
        s.rectifyOrangeRight = p[BossVictoryPaletteSlot.OrangeRight];
        s.rectifyGlowColor = p[BossVictoryPaletteSlot.Glow];
        s.crtOffGlowColor = p[BossVictoryPaletteSlot.CrtGlow];
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssetIfDirty(s);

        var log = new StringBuilder("[BossVictory271] 공용 팔레트 = 팔라딘 기준으로 구움 — 굽기 전후 최종 15색 최대 채널 오차:");
        for (int i = 0; i < Characters.Length; i++)
        {
            BossVictoryPalette after = BossVictoryPaletteCalc.Compute(s, s.ProfileFor(Characters[i]));
            float worst = 0f;
            BossVictoryPaletteSlot worstSlot = BossVictoryPaletteSlot.Amber;
            foreach (BossVictoryPaletteSlot slot in BossVictoryPalette.Slots)
            {
                Color a = before[i][slot], b = after[slot];
                float d = Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));
                if (d > worst) { worst = d; worstSlot = slot; }
            }
            log.Append($"\n  {Characters[i]}: {worst:0.000000} ({worstSlot})");
        }
        log.Append($"\n  새 공용 호박색 = {s.amberColor} · TV 꺼짐 = {s.crtOffGlowColor} · 역산 색조 차이 = {BossVictoryPaletteCalc.ReferenceContrastHueOffset(s):0.0000}°");
        Debug.Log(log.ToString());
    }
}
