using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 프로젝트가 기대하는 스크립팅 심볼을 대상 플랫폼마다 보장한다.
    /// UNITASK_DOTWEEN_SUPPORT: DOTween을 Asset Store(Assets/Plugins)로 들여와 UniTask가 자동으로 켜지 못하므로 직접 켠다.
    /// DOTween 쪽은 Modules 폴더의 DOTween.Modules.asmdef(DOTween 설정의 Create ASMDEF와 같은 것)가 필요하다.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectDefines
    {
        static readonly string[] Symbols = { "UNITASK_DOTWEEN_SUPPORT" };
        static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS };

        static ProjectDefines()
        {
            foreach (var target in Targets)
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
                var missing = Array.FindAll(Symbols, symbol => Array.IndexOf(current, symbol) < 0);
                if (missing.Length == 0) continue;

                var merged = new string[current.Length + missing.Length];
                current.CopyTo(merged, 0);
                missing.CopyTo(merged, current.Length);
                PlayerSettings.SetScriptingDefineSymbols(target, merged);
                Debug.Log($"[ProjectDefines] {target.TargetName}: {string.Join(", ", missing)} 추가");
            }
        }
    }
}
