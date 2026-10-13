using System.Collections.Generic;
using Game.UI;
using HarmonyLib;
using Tutorial;
using UnityEngine;
using UnityEngine.UI;

namespace UpdatedCamera
{
    // Small changes to the main screen: hide the company logo, and move/scale the contracts box.
    internal static class HudTweaks
    {
        private static readonly AccessTools.FieldRef<UIManager, Image> LogoField =
            AccessTools.FieldRefAccess<UIManager, Image>("corporationLogo");

        private static CurrentContractListMainUI _contracts;
        private static float _nextContractsSearch;

        // The object currently being moved, and how it looked before we touched it.
        private static RectTransform _moved;
        private static Vector2 _originalPosition;
        private static Vector3 _originalScale;
        private static LayoutElement _addedLayoutElement;
        private static LayoutElement _touchedLayoutElement;
        private static bool _hadLayoutElementIgnore;

        // Logo: everything drawn under the chosen ancestor gets hidden (the logo has a frame
        // around it that isn't part of the logo Image itself).
        private static Transform _logoHidden;
        private static readonly List<Graphic> HiddenGraphics = new List<Graphic>();

        public static void LateUpdate()
        {
            ApplyLogo();
            ApplyContracts();
        }

        private static void ApplyLogo()
        {
            if (SerializedMonoBehaviourSingleton<UIManager>.InstanceIsNull)
            {
                return;
            }
            Image logo = LogoField(SerializedMonoBehaviourSingleton<UIManager>.Instance);
            if (logo == null)
            {
                return;
            }
            Transform wanted = Settings.HideLogo.Value ? AncestorAt(logo.transform, Settings.LogoLevel.Value) : null;
            if (wanted != _logoHidden)
            {
                ShowLogoGraphics();
                _logoHidden = wanted;
            }
            if (_logoHidden != null)
            {
                // Disable the images rather than the objects, so the game's scripts keep running.
                // Re-checked every frame because the game may re-enable or add some.
                foreach (Graphic g in _logoHidden.GetComponentsInChildren<Graphic>())
                {
                    if (g.enabled)
                    {
                        g.enabled = false;
                        HiddenGraphics.Add(g);
                    }
                }
            }
        }

        private static void ShowLogoGraphics()
        {
            foreach (Graphic g in HiddenGraphics)
            {
                if (g != null)
                {
                    g.enabled = true;
                }
            }
            HiddenGraphics.Clear();
        }

        private static void ApplyContracts()
        {
            if (_contracts == null)
            {
                // FindObjectOfType is slow; only look a few times a second until the HUD exists.
                if (Time.unscaledTime < _nextContractsSearch)
                {
                    return;
                }
                _nextContractsSearch = Time.unscaledTime + 1f;
                _contracts = Object.FindObjectOfType<CurrentContractListMainUI>();
                _moved = null;
                if (_contracts == null)
                {
                    return;
                }
            }

            RectTransform wanted = AncestorAt(_contracts.transform, Settings.ContractsLevel.Value);
            if (wanted != _moved)
            {
                Restore();
                Capture(wanted);
            }
            if (_moved == null)
            {
                return;
            }

            float scale = Settings.ContractsScale.Value;
            Vector2 position = _originalPosition + new Vector2(Settings.ContractsX.Value, Settings.ContractsY.Value);
            _moved.anchoredPosition = position;
            _moved.localScale = _originalScale * scale;

            // The tutorial advisor sits where the moved box now is; slide the box below it while
            // it's showing. Unscaled time, because the tutorial often pauses the game.
            float target = AdvisorPushDown();
            float t = 1f - Mathf.Exp(-AdvisorSlideSpeed * Time.unscaledDeltaTime);
            _advisorSlide = Mathf.Abs(target - _advisorSlide) < 0.5f ? target : Mathf.Lerp(_advisorSlide, target, t);
            _moved.anchoredPosition = position - new Vector2(0f, _advisorSlide);
        }

        // Gap between the advisor's bottom edge and the contracts box, in the box's parent units.
        private const float AdvisorGap = 10f;
        private const float AdvisorSlideSpeed = 12f;

        // Parts of the advisor that are actually visible; its "Gradient" child is a 1500px backdrop.
        private static readonly string[] AdvisorParts = { "ImageFrame", "ScrollFrame", "ScrollFrame/ButtonSkip" };

        private static float _advisorSlide;
        private static readonly Vector3[] Corners = new Vector3[4];

        // How far down (in the box's parent units) the box must move to clear the advisor,
        // measured with the box at its normal moved position. 0 when the advisor isn't showing.
        private static float AdvisorPushDown()
        {
            if (SerializedMonoBehaviourSingleton<TutorialManager>.InstanceIsNull)
            {
                return 0f;
            }
            TutorialWindowAdvisor advisor = SerializedMonoBehaviourSingleton<TutorialManager>.Instance.TutorialWindowAdvisor;
            if (advisor == null || !advisor.gameObject.activeInHierarchy || !(_moved.parent is RectTransform parent))
            {
                return 0f;
            }

            float advisorBottom = float.MaxValue;
            float advisorLeft = float.MaxValue;
            float advisorRight = float.MinValue;
            foreach (string path in AdvisorParts)
            {
                Transform part = advisor.transform.Find(path);
                if (part is RectTransform rect && part.gameObject.activeInHierarchy)
                {
                    rect.GetWorldCorners(Corners);
                    Vector3 min = parent.InverseTransformPoint(Corners[0]);
                    Vector3 max = parent.InverseTransformPoint(Corners[2]);
                    advisorBottom = Mathf.Min(advisorBottom, min.y);
                    advisorLeft = Mathf.Min(advisorLeft, min.x);
                    advisorRight = Mathf.Max(advisorRight, max.x);
                }
            }
            if (advisorBottom == float.MaxValue)
            {
                return 0f;
            }

            _moved.GetWorldCorners(Corners);
            Vector3 boxMin = parent.InverseTransformPoint(Corners[0]);
            Vector3 boxMax = parent.InverseTransformPoint(Corners[2]);
            // A box moved off to the side doesn't need to make room.
            if (boxMax.x <= advisorLeft || boxMin.x >= advisorRight)
            {
                return 0f;
            }
            float push = Mathf.Max(0f, boxMax.y - (advisorBottom - AdvisorGap));

            // Never push the bottom of the box off the screen; if there isn't room, the advisor
            // (drawn on top) covers the top of the box instead.
            Canvas canvas = _moved.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.rootCanvas.transform is RectTransform screen)
            {
                screen.GetWorldCorners(Corners);
                float screenBottom = parent.InverseTransformPoint(Corners[0]).y;
                push = Mathf.Min(push, Mathf.Max(0f, boxMin.y - screenBottom));
            }
            return push;
        }

        // Level 0 is the contract list itself, 1 its parent, and so on (stops at the canvas).
        public static RectTransform AncestorAt(Transform start, int level)
        {
            Transform t = start;
            for (int i = 0; i < level && t.parent != null && t.parent.GetComponent<Canvas>() == null; i++)
            {
                t = t.parent;
            }
            return t as RectTransform;
        }

        public static Transform ContractsList => _contracts != null ? _contracts.transform : null;

        private static void Capture(RectTransform target)
        {
            _moved = target;
            if (target == null)
            {
                return;
            }
            // A layout group on the parent would snap the box back every frame; opt it out.
            if (target.parent != null && target.parent.GetComponent<LayoutGroup>() != null)
            {
                LayoutElement element = target.GetComponent<LayoutElement>();
                if (element == null)
                {
                    element = _addedLayoutElement = target.gameObject.AddComponent<LayoutElement>();
                }
                _touchedLayoutElement = element;
                _hadLayoutElementIgnore = element.ignoreLayout;
                element.ignoreLayout = true;
            }
            _originalPosition = target.anchoredPosition;
            _originalScale = target.localScale;
        }

        private static void Restore()
        {
            if (_moved == null)
            {
                return;
            }
            _moved.anchoredPosition = _originalPosition;
            _moved.localScale = _originalScale;
            if (_addedLayoutElement != null)
            {
                Object.Destroy(_addedLayoutElement);
            }
            else if (_touchedLayoutElement != null)
            {
                _touchedLayoutElement.ignoreLayout = _hadLayoutElementIgnore;
            }
            _addedLayoutElement = null;
            _touchedLayoutElement = null;
            _moved = null;
        }
    }
}
