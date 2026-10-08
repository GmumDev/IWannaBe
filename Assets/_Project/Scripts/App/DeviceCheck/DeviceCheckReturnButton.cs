using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IWannabe.App
{
    /// <summary>
    /// 기기 점검에서 로비로 나갔을 때 로비 오른쪽 위에 "기기 점검으로" 버튼을 띄운다. 씬이 바뀌어도 남고, 로비에서만 보인다
    /// (스테이지 중에는 숨겨 플레이를 가리지 않는다). 누르면 기기 점검 씬으로 돌아가고 사라진다.
    /// </summary>
    public sealed class DeviceCheckReturnButton : MonoBehaviour
    {
        string lobbyScene, returnScene;
        GUIStyle style;

        public static void Show(string lobbySceneName, string returnSceneName)
        {
            var existing = FindAnyObjectByType<DeviceCheckReturnButton>();
            var button = existing != null ? existing : new GameObject("DeviceCheckReturnButton").AddComponent<DeviceCheckReturnButton>();
            DontDestroyOnLoad(button.gameObject);
            button.lobbyScene = lobbySceneName;
            button.returnScene = returnSceneName;
        }

        void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != lobbyScene) return;
            if (StageFlow.Instance != null && StageFlow.Instance.IsTransitioning) return;
            if (style == null) style = new GUIStyle(GUI.skin.button) { fontSize = 20 };

            float scale = Mathf.Min(Screen.width, Screen.height) / 720f;
            var safe = Screen.safeArea;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var rect = new Rect(safe.xMax / scale - 228, (Screen.height - safe.yMax) / scale + 8, 220, 56);
            if (GUI.Button(rect, "기기 점검으로", style))
            {
                SceneManager.LoadScene(returnScene);
                Destroy(gameObject);
            }
        }
    }
}
