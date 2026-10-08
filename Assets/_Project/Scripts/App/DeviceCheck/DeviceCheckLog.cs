using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IWannabe.App
{
    /// <summary>
    /// 기기 점검 결과 기록. 화면(결과 탭), 로그([DeviceCheck], adb logcat -s Unity), 파일(persistentDataPath/device_check.txt)에 함께 남긴다.
    /// 처음 쓸 때 파일에 쌓인 이전 실행의 기록을 읽어 와서, 앱을 다시 켜도 결과 탭과 클립보드 복사에 전체 기록이 나온다.
    /// </summary>
    public static class DeviceCheckLog
    {
        static readonly List<string> lines = new List<string>();
        static bool loaded;

        public static IReadOnlyList<string> Lines
        {
            get
            {
                EnsureLoaded();
                return lines;
            }
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "device_check.txt");

        public static void Add(string line)
        {
            EnsureLoaded();
            string stamped = $"{DateTime.Now:MM-dd HH:mm:ss} {line}";
            lines.Add(stamped);
            Debug.Log($"[DeviceCheck] {line}");
            try
            {
                File.AppendAllText(FilePath, stamped + "\n");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[DeviceCheck] 파일에 쓰지 못함: {e.Message}");
            }
        }

        /// <summary>화면의 기록과 파일을 모두 지운다.</summary>
        public static void Clear()
        {
            lines.Clear();
            loaded = true;
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[DeviceCheck] 파일을 지우지 못함: {e.Message}");
            }
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath));
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[DeviceCheck] 파일을 읽지 못함: {e.Message}");
            }
        }
    }
}
