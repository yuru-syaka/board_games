using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// サイト（board_games/web/play/&lt;slug&gt;/）に置く WebGL ビルドを作る。
/// 正本は board_games リポジトリの unity/Editor/WebBuild.cs。各ゲームの Assets/Editor/ にコピーして使う。
/// ゲームごとの値は同じフォルダの WebBuildSettings.json に書く。
///
/// 実行例（Unity CLI）:
///   unity build &lt;project&gt; --target WebGL --execute-method WebBuild.Build --output-path &lt;board_games&gt;/web/play/&lt;slug&gt;
/// </summary>
public static class WebBuild
{
    const string TemplateName = "PROJECT:BoardGames";

    [Serializable]
    class Settings
    {
        public string title; // 画面上部に出すゲーム名（日本語可）
        public string page;  // web/games/ 以下の紹介ページのファイル名
    }

    public static void Build()
    {
        string output = Arg("-buildOutput");
        if (string.IsNullOrEmpty(output)) Fail("-buildOutput (--output-path) を指定してください。");

        string settingsPath = Path.Combine(Application.dataPath, "Editor", "WebBuildSettings.json");
        if (!File.Exists(settingsPath)) Fail($"{settingsPath} がありません。");
        var settings = JsonUtility.FromJson<Settings>(File.ReadAllText(settingsPath));

        PlayerSettings.WebGL.template = TemplateName;
        PlayerSettings.SetTemplateCustomValue("GAME_TITLE", settings.title);
        PlayerSettings.SetTemplateCustomValue("GAME_PAGE", settings.page);

        // CloudFront / GitHub Pages のどちらでも、サーバー側の設定なしで動くようにする:
        // gzip で圧縮し、ブラウザが展開できない場合はローダーが JS で展開する（Decompression Fallback）。
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        // ファイル名にハッシュを付け、更新時に古いキャッシュが使われないようにする
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.dataCaching = true;

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) Fail("Build Settings に有効なシーンがありません。");

        if (Directory.Exists(output)) Directory.Delete(output, true);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });

        Debug.Log($"[WebBuild] {report.summary.result}: {output} ({report.summary.totalSize / 1024 / 1024} MB, {report.summary.totalTime})");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static void Fail(string message)
    {
        Debug.LogError("[WebBuild] " + message);
        EditorApplication.Exit(1);
        throw new InvalidOperationException(message);
    }
}
