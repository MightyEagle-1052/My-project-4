using System;
using SnakeCatcher.Editor;
using UnityEditor;
using UnityEngine;

public static class SetupPlayerCamera
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before setting up the camera.");

        PlayerMovementSetup.CreatePlayground();
        PlayerSettings.productName = "Snake Catcher";
        EditorSettings.projectGenerationRootNamespace = "SnakeCatcher";
        EditorSettings.enterPlayModeOptionsEnabled = false;
        Unity.CodeEditor.CodeEditor.SetExternalScriptEditor(
            @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe");
        Unity.CodeEditor.CodeEditor.CurrentEditor.SyncAll();
        AssetDatabase.SaveAssets();
        return "Player camera and movement playground created; Visual Studio selected.";
    }
}
