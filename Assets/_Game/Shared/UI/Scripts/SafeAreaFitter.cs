using UnityEngine;

namespace KingdomRuler.Shared.UI
{
    /// <summary>
    /// Insets a RectTransform to the device's safe area, keeping its content clear of notches,
    /// rounded corners, the iPhone home indicator and the Android gesture bar.
    /// </summary>
    /// <remarks>
    /// <para><c>GDD.md</c> §3 makes safe area non-negotiable across 19.5:9 down to 16:9. The
    /// bottom nav bar is the sharpest case in the whole game: it sits exactly where the gesture
    /// bar lives, so without this its tap targets land under system UI on most modern phones —
    /// and the Editor's Game view shows nothing wrong, because <c>Screen.safeArea</c> is the
    /// full rect there unless a device simulator resolution is selected.</para>
    ///
    /// <para>Apply it to a <b>full-screen child</b> of the Canvas, with the actual content
    /// inside that child. Applying it to a Canvas's own RectTransform does nothing — the Canvas
    /// drives its own rect.</para>
    ///
    /// <para><b>Inset the content, not the background.</b> A bar or panel that paints a
    /// background must keep that background edge-to-edge while only its controls move inward,
    /// or the device's own strip shows through underneath as a mismatched band. Put the
    /// background inside this container but give it a rect that deliberately overshoots past
    /// the inset edge — UGUI does not clip to the canvas, so the excess simply falls off the
    /// screen. <c>BottomNavBar.prefab</c> is the worked example (<c>ARCHITECTURE.md</c> §4.6).</para>
    ///
    /// <para>Turn <see cref="_applyHorizontal"/> off for a full-width bar. In portrait the
    /// horizontal insets are zero anyway, and leaving it on would pull a full-bleed background
    /// away from the screen edges the moment a device does report one.</para>
    ///
    /// <para>The re-check runs in <c>Update</c> and is two struct comparisons. It cannot be
    /// event-driven: Unity raises no callback for a safe-area change, and the value moves on
    /// rotation, on split-screen resize, and on the Editor's Game view being resized or
    /// switched to a different simulated device. Anchors are only written when something
    /// actually changed, so the steady-state cost is the comparison alone.</para>
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
            // Force the next check to write: the object may have been re-enabled after the
            // resolution changed while it was off.
            _appliedSafeArea = new Rect(0f, 0f, 0f, 0f);
            _appliedPadding  = null;
            Apply();
        }

#if UNITY_EDITOR
        // So a designer dragging the padding in the Inspector sees it immediately, rather than
        // only after the next resolution change.
        private void OnValidate() => _appliedPadding = null;
#endif

        private void Update() => Apply();

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;

            var safeArea   = Screen.safeArea;
            var resolution = new Vector2Int(Screen.width, Screen.height);

            // Resolution is part of the key, not just the safe area: the same safe-area rect in
            // pixels means different anchors at a different screen size, and switching device
            // profiles in the simulator can land on exactly that case.
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

            // The safe area is expressed as anchors (resolution-independent fractions); the
            // padding is expressed as offsets (canvas units, so it matches authored sizes like
            // the nav bar's height). Mixing the two is deliberate — each is in the unit that
            // makes it correct across devices.
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
