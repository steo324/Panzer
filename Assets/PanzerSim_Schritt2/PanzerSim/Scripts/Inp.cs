using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Kleine Eingabe-Hilfe: funktioniert mit dem neuen Input System UND dem alten Input Manager.
    /// </summary>
    public static class Inp
    {
#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.KeyControl Map(KeyCode k)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return null;
            switch (k)
            {
                case KeyCode.W: return kb.wKey;
                case KeyCode.A: return kb.aKey;
                case KeyCode.S: return kb.sKey;
                case KeyCode.D: return kb.dKey;
                case KeyCode.Q: return kb.qKey;
                case KeyCode.E: return kb.eKey;
                case KeyCode.R: return kb.rKey;
                case KeyCode.C: return kb.cKey;
                case KeyCode.F: return kb.fKey;
                case KeyCode.G: return kb.gKey;
                case KeyCode.N: return kb.nKey;
                case KeyCode.T: return kb.tKey;
                case KeyCode.X: return kb.xKey;
                case KeyCode.V: return kb.vKey;
                case KeyCode.P: return kb.pKey;
                case KeyCode.B: return kb.bKey;
                case KeyCode.K: return kb.kKey;
                case KeyCode.L: return kb.lKey;
                case KeyCode.F3: return kb.f3Key;
                case KeyCode.Space: return kb.spaceKey;
                case KeyCode.Escape: return kb.escapeKey;
                case KeyCode.LeftShift: return kb.leftShiftKey;
                case KeyCode.Alpha1: return kb.digit1Key;
                case KeyCode.Alpha2: return kb.digit2Key;
                case KeyCode.Alpha3: return kb.digit3Key;
                case KeyCode.Alpha4: return kb.digit4Key;
                case KeyCode.Alpha5: return kb.digit5Key;
                case KeyCode.Alpha6: return kb.digit6Key;
                case KeyCode.Alpha7: return kb.digit7Key;
                case KeyCode.Alpha8: return kb.digit8Key;
                case KeyCode.Alpha9: return kb.digit9Key;
                default: return null;
            }
        }

        public static bool Held(KeyCode k) { var c = Map(k); return c != null && c.isPressed; }
        public static bool Down(KeyCode k) { var c = Map(k); return c != null && c.wasPressedThisFrame; }

        static UnityEngine.InputSystem.Mouse M => UnityEngine.InputSystem.Mouse.current;
        public static bool FireDown => M != null && M.leftButton.wasPressedThisFrame;
        public static bool LookHeld => M != null && M.rightButton.isPressed;
        /// <summary>Turm-MG: rechte Maustaste halten.</summary>
        public static bool MgHeld => M != null && M.rightButton.isPressed;
        public static Vector2 MouseDelta => M != null ? M.delta.ReadValue() * 0.1f : Vector2.zero;
        public static float Scroll => M != null ? Mathf.Sign(M.scroll.ReadValue().y) * (Mathf.Abs(M.scroll.ReadValue().y) > 0.01f ? 1f : 0f) : 0f;
#else
        public static bool Held(KeyCode k) => Input.GetKey(k);
        public static bool Down(KeyCode k) => Input.GetKeyDown(k);
        public static bool FireDown => Input.GetMouseButtonDown(0);
        public static bool LookHeld => Input.GetMouseButton(1);
        public static bool MgHeld => Input.GetMouseButton(1);
        public static Vector2 MouseDelta => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        public static float Scroll => Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f ? Mathf.Sign(Input.mouseScrollDelta.y) : 0f;
#endif
    }
}
