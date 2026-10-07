using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace IWannabe.Rhythm
{
    public readonly struct RhythmInputEvent
    {
        /// <summary>입력 이벤트가 발생한 시각(realtimeSinceStartup 기준). 프레임 시각이 아니다.</summary>
        public readonly double Time;
        /// <summary>입력원(키·손가락·버튼) 식별자. 홀드는 누른 입력원이 떼야 끝난다.</summary>
        public readonly int Source;

        public RhythmInputEvent(double time, int source)
        {
            Time = time;
            Source = source;
        }
    }

    /// <summary>
    /// 키보드·마우스·패드·터치를 "버튼 하나"로 묶는다.
    /// Press 액션은 PassThrough라서, 다른 키나 손가락을 번갈아 눌러도 누를 때마다 입력이 들어온다.
    /// </summary>
    public sealed class RhythmInput : MonoBehaviour
    {
        // 터치 기기에서 터치가 마우스 입력으로도 들어오는 경우를 걸러낸다.
        const double TouchMouseDedupSeconds = 0.2;

        [SerializeField] InputAction press = new InputAction("Press", InputActionType.PassThrough);
        [SerializeField] InputAction pause = new InputAction("Pause", InputActionType.Button);

        public event Action<RhythmInputEvent> Pressed;
        public event Action<RhythmInputEvent> Released;
        public event Action PauseRequested;

        /// <summary>false면 누름을 무시한다(로딩·일시정지·결과 화면).</summary>
        public bool GameplayEnabled { get; set; }

        readonly Dictionary<InputControl, int> sourceIds = new Dictionary<InputControl, int>();
        readonly HashSet<InputControl> held = new HashSet<InputControl>();
        readonly HashSet<InputControl> ignored = new HashSet<InputControl>();
        double lastTouchTime = double.NegativeInfinity;

        public void ResetBindingsToDefault()
        {
            press = new InputAction("Press", InputActionType.PassThrough);
            press.AddBinding("<Keyboard>/space");
            press.AddBinding("<Keyboard>/enter");
            press.AddBinding("<Keyboard>/f");
            press.AddBinding("<Keyboard>/j");
            press.AddBinding("<Mouse>/leftButton");
            press.AddBinding("<Gamepad>/buttonSouth");
            press.AddBinding("<Touchscreen>/touch*/press");

            pause = new InputAction("Pause", InputActionType.Button);
            pause.AddBinding("<Keyboard>/escape");
            pause.AddBinding("<Gamepad>/start");
        }

        void Awake()
        {
            if (press.bindings.Count == 0) ResetBindingsToDefault();
        }

        void OnEnable()
        {
            // PassThrough는 누름·뗌 모두 performed로 오고, 포커스 상실 등으로 장치가 리셋되면 canceled로 온다.
            press.performed += OnPress;
            press.canceled += OnPress;
            pause.performed += OnPause;
            press.Enable();
            pause.Enable();
        }

        void OnDisable()
        {
            press.performed -= OnPress;
            press.canceled -= OnPress;
            pause.performed -= OnPause;
            press.Disable();
            pause.Disable();
            held.Clear();
            ignored.Clear();
        }

        void OnPause(InputAction.CallbackContext context) => PauseRequested?.Invoke();

        void OnPress(InputAction.CallbackContext context)
        {
            var control = context.control;
            bool isDown = context.ReadValueAsButton();
            if (isDown == held.Contains(control)) return;

            if (isDown)
            {
                held.Add(control);
                if (control.device is Touchscreen)
                {
                    lastTouchTime = context.time;
                }
                else if (control.device is Mouse && context.time - lastTouchTime < TouchMouseDedupSeconds)
                {
                    ignored.Add(control);
                    return;
                }

                if (!GameplayEnabled || IsOverBlockArea(control))
                {
                    ignored.Add(control);
                    return;
                }
                Pressed?.Invoke(new RhythmInputEvent(context.time, SourceId(control)));
            }
            else
            {
                held.Remove(control);
                if (ignored.Remove(control)) return;
                Released?.Invoke(new RhythmInputEvent(context.time, SourceId(control)));
            }
        }

        static bool IsOverBlockArea(InputControl control)
        {
            if (control.parent is TouchControl touch)
                return InputBlockArea.Contains(touch.position.ReadValue());
            if (control.device is Pointer pointer)
                return InputBlockArea.Contains(pointer.position.ReadValue());
            return false;
        }

        int SourceId(InputControl control)
        {
            if (!sourceIds.TryGetValue(control, out int id))
            {
                id = sourceIds.Count + 1;
                sourceIds[control] = id;
            }
            return id;
        }
    }
}
