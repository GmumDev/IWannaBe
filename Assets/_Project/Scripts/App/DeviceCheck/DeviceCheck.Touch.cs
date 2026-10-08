using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IWannabe.App
{
    /// <summary>
    /// 터치 점검. 입력 이벤트 시각(<c>context.time</c>, 판정에 쓰는 값)이 실제 터치 시각을 얼마나 담는지 본다.
    /// <list type="bullet">
    /// <item>처리 지연: 콜백을 받은 시각 - 이벤트 시각. 이벤트 시각이 OS가 터치를 받은 시각이면 0~한 프레임 사이로 퍼지고,
    /// 프레임 처리 시각으로 찍힌다면 늘 0 근처다(그러면 판정이 프레임 단위로 뭉개진다).</item>
    /// <item>이동 이벤트 간격: 문지를 때 위치 이벤트 시각 간격. 터치 샘플링(예: 120·240Hz)이 그대로 오는지, 프레임마다 하나로 합쳐지는지.</item>
    /// </list>
    /// </summary>
    public sealed partial class DeviceCheck
    {
        InputAction touchPress;
        InputAction touchMove;
        readonly List<double> pressLags = new List<double>();
        readonly List<double> moveLags = new List<double>();
        readonly List<double> moveIntervals = new List<double>();
        readonly List<double> frameIntervals = new List<double>();
        double lastMoveTime = double.NaN;
        double lastFrameTime = double.NaN;

        void EnableTouchProbe()
        {
            touchPress = new InputAction("ProbePress", InputActionType.PassThrough);
            touchPress.AddBinding("<Touchscreen>/touch*/press");
            touchPress.AddBinding("<Mouse>/leftButton");
            touchMove = new InputAction("ProbeMove", InputActionType.PassThrough);
            touchMove.AddBinding("<Touchscreen>/primaryTouch/position");
            touchPress.performed += OnProbePress;
            touchMove.performed += OnProbeMove;
            touchPress.Enable();
            touchMove.Enable();
        }

        void DisableTouchProbe()
        {
            touchPress.performed -= OnProbePress;
            touchMove.performed -= OnProbeMove;
            touchPress.Dispose();
            touchMove.Dispose();
        }

        void OnProbePress(InputAction.CallbackContext context)
        {
            if (page != Page.Touch || !context.ReadValueAsButton()) return;
            pressLags.Add(Time.realtimeSinceStartupAsDouble - context.time);
        }

        void OnProbeMove(InputAction.CallbackContext context)
        {
            if (page != Page.Touch) return;
            moveLags.Add(Time.realtimeSinceStartupAsDouble - context.time);
            if (!double.IsNaN(lastMoveTime) && context.time > lastMoveTime && context.time - lastMoveTime < 0.1)
                moveIntervals.Add(context.time - lastMoveTime);
            lastMoveTime = context.time;
        }

        void LateUpdate()
        {
            if (page != Page.Touch) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!double.IsNaN(lastFrameTime) && frameIntervals.Count < 2000) frameIntervals.Add(now - lastFrameTime);
            lastFrameTime = now;
        }

        void DrawTouchPage()
        {
            GUILayout.Label("화면을 여러 번 톡톡 누르고, 한 손가락으로 몇 초 문지른다.", label);
            GUILayout.Label($"누름 처리 지연: {Summarize(pressLags, "ms")}", label);
            GUILayout.Label($"이동 처리 지연: {Summarize(moveLags, "ms")}", label);
            GUILayout.Label($"이동 이벤트 간격: {Summarize(moveIntervals, "ms")}", label);
            GUILayout.Label($"프레임 간격: {Summarize(frameIntervals, "ms")}", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("기록", button, GUILayout.Height(56)))
            {
                DeviceCheckLog.Add($"터치: 누름 처리 지연 {Summarize(pressLags, "ms")} / 이동 처리 지연 {Summarize(moveLags, "ms")} / " +
                                   $"이동 이벤트 간격 {Summarize(moveIntervals, "ms")} / 프레임 간격 {Summarize(frameIntervals, "ms")}");
            }
            if (GUILayout.Button("초기화", button, GUILayout.Height(56)))
            {
                pressLags.Clear();
                moveLags.Clear();
                moveIntervals.Clear();
                frameIntervals.Clear();
                lastMoveTime = double.NaN;
            }
            GUILayout.EndHorizontal();
        }
    }
}
