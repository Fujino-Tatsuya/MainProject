using UnityEditor;
using UnityEngine;

/// <summary>
/// 타이틀 벽 모니터 존 투어 영상(할 일 254) 임포트 설정 — <c>Assets/50.Art/Video/Title/</c> 아래 영상만.
/// 원본 1280×720 을 <b>절반(640×360)으로 트랜스코드</b>하고 소리는 가져오지 않는다.
/// </summary>
/// <remarks>
/// 근거: 벽 모니터는 화면에서 작고(가장 큰 monitor (8) 도 화면 높이의 몇 분의 1) 재생 RT 도 640×360 이라 720p 해상도는 보이지 않는다.
/// 3대를 동시에 디코딩하므로 픽셀 수 ¼ 이 디코딩·업로드 비용과 빌드 용량(원본 합 56MB)을 함께 줄인다. 원본 mp4 는 SVN 에 그대로 둔다.
/// 영상 .meta 는 SVN(아트)이 소유하므로 설정을 .meta 에 손으로 박지 않고 여기서 매 임포트마다 강제한다 —
/// 설정을 바꾸면 <see cref="Version"/> 을 올려 재임포트시킨다.
/// </remarks>
public sealed class TitleWallVideoImport : AssetPostprocessor
{
    public const string Folder = "Assets/50.Art/Video/Title/";
    private const uint Version = 1;

    public override uint GetVersion() => Version;

    private void OnPreprocessAsset()
    {
        if (!assetPath.StartsWith(Folder)) return;
        if (assetImporter is not VideoClipImporter importer) return;

        importer.importAudio = false;
        VideoImporterTargetSettings s = importer.defaultTargetSettings ?? new VideoImporterTargetSettings();
        s.enableTranscoding = true;
        s.codec = VideoCodec.Auto;
        s.resizeMode = VideoResizeMode.HalfRes; // 1280×720 → 640×360
        s.aspectRatio = VideoEncodeAspectRatio.NoScaling;
        s.bitrateMode = VideoBitrateMode.Medium;
        s.spatialQuality = VideoSpatialQuality.MediumSpatialQuality;
        importer.defaultTargetSettings = s;
    }
}
