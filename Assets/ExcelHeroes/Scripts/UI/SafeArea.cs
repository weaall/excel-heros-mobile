using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// Keeps the UI out from under the camera cutout and the gesture bar.
    ///
    /// A phone is not a rectangle you get all of. The status bar and the notch eat the top, the
    /// home indicator eats the bottom, and on some devices the rounded corners eat a little of
    /// both. Without this the green app bar sits under the clock and the 홈 ▲ command bar sits
    /// under the gesture bar, where a swipe goes to the system rather than to the game.
    ///
    /// Unity reports the usable rectangle as Screen.safeArea in device pixels; UI Toolkit lays out
    /// in the panel's own units, so the insets are converted through the panel scale. It is
    /// re-applied on orientation and resolution changes, because a device that folds or rotates
    /// reports a different rectangle afterwards.
    /// </summary>
    public class SafeArea : MonoBehaviour
    {
        VisualElement _root;
        Rect _applied;
        ScreenOrientation _orientation;

        public void Bind(VisualElement root)
        {
            _root = root;
            Apply();
        }

        void Update()
        {
            if (_root == null) return;
            if (Screen.safeArea == _applied && Screen.orientation == _orientation) return;
            Apply();
        }

        void Apply()
        {
            var safe = Screen.safeArea;
            _applied = safe;
            _orientation = Screen.orientation;

            // Panel units per device pixel. The root's resolved width is in panel units and
            // Screen.width is in device pixels, so their ratio converts one to the other.
            var width = _root.resolvedStyle.width;
            var scale = Screen.width > 0 && width > 1f ? width / Screen.width : 1f;

            _root.style.paddingLeft = safe.xMin * scale;
            _root.style.paddingRight = (Screen.width - safe.xMax) * scale;
            // Screen space has its origin at the bottom left; the top inset is what is above yMax.
            _root.style.paddingTop = (Screen.height - safe.yMax) * scale;
            _root.style.paddingBottom = safe.yMin * scale;
        }
    }
}
