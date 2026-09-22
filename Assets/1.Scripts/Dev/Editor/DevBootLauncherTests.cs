using NUnit.Framework;
using UnityEditor;

public sealed class DevBootLauncherTests
{
    private const string TargetPath = "Assets/0.Scenes/Debug/PlayerScene.unity";

    [Test]
    public void MissingScene_IsAppendedAndEnabled()
    {
        var original = new[] { new EditorBuildSettingsScene("Assets/0.Scenes/MainFlow/4.MapScene.unity", true) };

        DevBootLauncher.BuildSceneAdjustment result = DevBootLauncher.EnsureSceneEnabled(original, TargetPath);

        Assert.That(result.Changed, Is.True);
        Assert.That(result.Scenes, Has.Length.EqualTo(2));
        Assert.That(result.Scenes[1].path, Is.EqualTo(TargetPath));
        Assert.That(result.Scenes[1].enabled, Is.True);
    }

    [Test]
    public void DisabledScene_IsEnabledWithoutMutatingInput()
    {
        var original = new[] { new EditorBuildSettingsScene(TargetPath, false) };

        DevBootLauncher.BuildSceneAdjustment result = DevBootLauncher.EnsureSceneEnabled(original, TargetPath);

        Assert.That(result.Changed, Is.True);
        Assert.That(result.Scenes[0].enabled, Is.True);
        Assert.That(original[0].enabled, Is.False);
    }

    [Test]
    public void AlreadyEnabledScene_ReportsNoChange()
    {
        var original = new[] { new EditorBuildSettingsScene(TargetPath, true) };

        DevBootLauncher.BuildSceneAdjustment result = DevBootLauncher.EnsureSceneEnabled(original, TargetPath);

        Assert.That(result.Changed, Is.False);
        Assert.That(result.Scenes, Has.Length.EqualTo(1));
        Assert.That(result.Scenes[0].enabled, Is.True);
    }

    [Test]
    public void ActiveProfileOverride_SelectsProfileSceneList()
    {
        DevBootLauncher.BuildSceneSourceKind source =
            DevBootLauncher.SelectBuildSceneSource(hasActiveProfile: true, overrideGlobalScenes: true);

        Assert.That(source, Is.EqualTo(DevBootLauncher.BuildSceneSourceKind.ActiveProfile));
    }
}
