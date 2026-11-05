using UnityEngine;

namespace KingdomRuler.Shared.UI
{
    /// <summary>
    /// Insets a RectTransform to the device's safe area, keeping its content clear of notches,
    /// rounded corners, the iPhone home indicator and the Android gesture bar.
    /// </summary>
    /// <remarks>
    /// <para>The Editor's Game view shows no problem: <c>Screen.safeArea</c> is the full rect there
    /// unless a device simulator resolution is selected. Check safe-area layout in the simulator.</para>
    ///
    /// <para>Apply it to a <b>full-screen child</b> of the Canvas, with the content inside it — the
    /// Canvas drives its own rect, so on the Canvas itself this does nothing.</para>
    ///
    /// <para><b>Inset the content, not the background</b>, or the device's strip shows through as a
    /// mismatched band. Keep a background inside this container but give it a rect that overshoots
    /// the inset edge; UGUI does not clip to the canvas. <c>BottomNavBar.prefab</c> is the worked
    /// example (<c>ARCHITECTURE.md</c> §4.6), and has <see cref="_applyHorizontal"/> off so its
    /// full-bleed background is never pulled in from the screen edges.</para>
    ///
    /// <para>Re-checked in <c>Update</c> because Unity raises no safe-area-changed callback
    /// (rotation, split-screen, a simulator switch). Anchors are only written on change, so the
    /// steady-state cost is two comparisons.</para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [Tooltip("Inset the left/right edges. Turn off for a bar that should span the full " +
                 "width and only needs the bottom kept clear.")]
        [SerializeField] private bool _applyHorizontal = true;

        [Tooltip("Inset the top/bottom edges. This is the one the bottom nav bar needs.")]
        [SerializeField] private bool _applyVertical = true;

        [Tooltip("Extra inset applied on top of the safe area, in canvas units (the same " +
                 "units as the reference resolution). A screen sets Bottom to the nav bar's " +
                 "height so its UI does not sit underneath the bar.")]
        [SerializeField] private RectOffset _padding = new();

        private RectTransform _rect;
        private Rect  _appliedSafeArea = new(0f, 0f, 0f, 0f);
        private Vector2Int _appliedResolution;
        private RectOffset _appliedPadding;

        private void Awake() => _rect = (RectTransform)transform;

        private void OnEnable()
        {
            // Force a write: the resolution may have changed while this was disabled.
            _appliedSafeArea = new Rect(0f, 0f, 0f, 0f);
            _appliedPadding  = null;
            Apply();
        }

#if UNITY_EDITOR
        // So padding edited in the Inspector applies immediately.
        private void OnValidate() => _appliedPadding = null;
#endif

        private void Update() => Apply();

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;

            var safeArea   = Screen.safeArea;
            var resolution = new Vector2Int(Screen.width, Screen.height);

            // Resolution is part of the key: the same safe-area rect means different anchors at a
            // different screen size.
            if (safeArea == _appliedSafeArea && resolution == _appliedResolution
                && SamePadding(_appliedPadding, _padding)) return;
            if (resolution.x <= 0 || resolution.y <= 0) return;   // minimised / not yet laid out

            _appliedSafeArea   = safeArea;
            _appliedResolution = resolution;
            _appliedPadding    = new RectOffset(_padding.left, _padding.right,
                                                _padding.top,  _padding.bottom);

            var min = new Vector2(safeArea.xMin / resolution.x, safeArea.yMin / resolution.y);
            var max = new Vector2(safeArea.xMax / resolution.x, safeArea.yMax / resolution.y);

            if (!_applyHorizontal) { min.x = 0f; max.x = 1f; }
            if (!_applyVertical)   { min.y = 0f; max.y = 1f; }

            _rect.anchorMin = min;
            _rect.anchorMax = max;

            // Safe area as anchors (resolution-independent), padding as offsets (canvas units, like
            // the nav bar's authored height) — each in the unit that stays correct across devices.
            _rect.offsetMin = new Vector2(_padding.left,   _padding.bottom);
            _rect.offsetMax = new Vector2(-_padding.right, -_padding.top);
        }

        private static bool SamePadding(RectOffset a, RectOffset b)
        {
            if (a == null || b == null) return false;
            return a.left == b.left && a.right == b.right
                && a.top  == b.top  && a.bottom == b.bottom;
        }
    }
}
