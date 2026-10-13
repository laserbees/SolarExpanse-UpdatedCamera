using System.Collections.Generic;
using Game.UI;
using HarmonyLib;
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
            _moved.anchoredPosition = _originalPosition + new Vector2(Settings.ContractsX.Value, Settings.ContractsY.Value);
            _moved.localScale = _originalScale * scale;
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
