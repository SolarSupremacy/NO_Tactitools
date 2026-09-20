using System;
using System.Collections.Generic;
using HarmonyLib;
using NO_Tactitools.Core;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NO_Tactitools.UI.HUD;

[HarmonyPatch(typeof(MainMenu), "Start")]
internal class BankIndicatorPlugin
{
    private static bool _initialized;

    private static void Postfix()
    {
        if (_initialized) return;

        Plugin.Log("[BI] Rotor Bank Indicator plugin starting !");
        Plugin.harmony.PatchAll(typeof(BankIndicatorComponent.OnPlatformStart));
        Plugin.harmony.PatchAll(typeof(BankIndicatorComponent.OnPlatformUpdate));
        _initialized = true;
        Plugin.Log("[BI] Rotor Bank Indicator plugin successfully started !");
    }
}

public class BankIndicatorComponent
{
    private static class LogicEngine
    {
        public static void Init()
        {
            InternalState.BIWidget?.Destroy();
            InternalState.BIWidget = null;
            InternalState.authorizedPlatforms =
                FileUtilities.GetListFromConfigFile("BankIndicator_AuthorizedPlatforms.txt");
            InternalState.isAuthorized =
                InternalState.authorizedPlatforms.Contains(GameBindings.Player.Aircraft.GetPlatformName());
            if (!InternalState.isAuthorized) return;

            InternalState.currentBankAngle = 0f;
            InternalState.maxBankAngle = (int)Mathf.Clamp(
                Mathf.Round(Plugin.bankIndicatorMaxBank.Value / 5f) * 5f,
                5,
                45);
            InternalState.currentX = Plugin.bankIndicatorPositionX.Value;
            InternalState.currentY = Plugin.bankIndicatorPositionY.Value;
        }

        public static void Update()
        {
            if (GameBindings.GameState.IsGamePaused()
                || GameBindings.Player.Aircraft.GetAircraft() == null
                || !InternalState.isAuthorized)
                return;
            Transform aircraftTransform = GameBindings.Player.Aircraft.GetAircraft().transform;
            float bank = aircraftTransform.localEulerAngles.z;
            if (bank > 180f) bank -= 360f;
            InternalState.currentBankAngle = bank;
            InternalState.needsUpdate = InternalState.currentX != Plugin.bankIndicatorPositionX.Value ||
                                        InternalState.currentY != Plugin.bankIndicatorPositionY.Value;
            if (InternalState.needsUpdate)
            {
                InternalState.currentX = Plugin.bankIndicatorPositionX.Value;
                InternalState.currentY = Plugin.bankIndicatorPositionY.Value;
            }
        }
    }

    public static class InternalState
    {
        public static float currentBankAngle;
        public static int maxBankAngle = 15;
        public static int currentX;
        public static int currentY;
        public static bool needsUpdate;
        public static bool isAuthorized;
        public static List<string> authorizedPlatforms = [];
        public static BankIndicatorWidget BIWidget;
    }

    private static class DisplayEngine
    {
        public static void Init()
        {
            if (!InternalState.isAuthorized) return;
            InternalState.BIWidget = new BankIndicatorWidget(UIBindings.Game.GetFlightHUDCenterTransform());
        }

        public static void Update()
        {
            if (GameBindings.GameState.IsGamePaused()
                || GameBindings.Player.Aircraft.GetAircraft() == null
                || !InternalState.isAuthorized)
                return;

            InternalState.BIWidget.UpdateDisplay(InternalState.currentBankAngle);
            if (InternalState.needsUpdate)
                InternalState.BIWidget.SetPosition(new Vector2(InternalState.currentX, InternalState.currentY));
        }
    }

    public class BankIndicatorWidget
    {
        public Image arc;
        public UIBindings.Draw.UILabel bankLabel;
        public GameObject containerObject;
        public RectTransform containerTransform;
        public List<UIBindings.Draw.UILine> increments = [];
        public Image needle;

        private readonly Vector3 needleBasePosition;
        private readonly Quaternion needleBaseRotation;

        public BankIndicatorWidget(Transform parent)
        {
            containerObject = new GameObject("i_RBI_Container", typeof(RectTransform));
            containerTransform = containerObject.GetComponent<RectTransform>();
            containerTransform.SetParent(parent, false);
            //containerTransform.localPosition = new Vector3(Plugin.bankIndicatorPositionX.Value, Plugin.bankIndicatorPositionY.Value, 0);
            containerTransform.anchoredPosition =
                new Vector2(Plugin.bankIndicatorPositionX.Value, Plugin.bankIndicatorPositionY.Value);
            containerTransform.localRotation = Quaternion.identity;
            containerTransform.localScale = Vector3.one;

            int radius = 70;

            needle = GameObject.Instantiate(
                UIBindings.Game.GetFlightHUDCenterTransform().Find("compass/compassPoint").GetComponent<Image>(),
                containerTransform);
            needle.name = "i_RBI_needle";
            // make it 2/3 the size of the compass needle and move it to the same position as the arc
            needle.rectTransform.localScale = Vector3.one * 0.66f;
            needle.rectTransform.anchoredPosition = new Vector2(0, -radius - 10);
            needle.color = new Color(needle.color.r, needle.color.g, needle.color.b,
                Plugin.bankIndicatorTransparency.Value);
            needle.material = UIBindings.Game.GetFlightHUDFontMaterial();

            needleBasePosition = needle.rectTransform.localPosition;
            needleBaseRotation = needle.rectTransform.localRotation;

            bankLabel = new UIBindings.Draw.UILabel(
                "i_RBI_bankLabel",
                new Vector2(0, -radius - 25),
                containerTransform,
                color: new Color(0f, 1f, 0f, Plugin.bankIndicatorTransparency.Value),
                fontSize: 34,
                backgroundOpacity: 0f,
                material: UIBindings.Game.GetFlightHUDFontMaterial(),
                context: ThemeManager.ThemeContext.HUD,
                styleLabel: UIBindings.Draw.StyleLabels[ThemeManager.ThemeContext.HUD]["HUD_TextMainColor"]
            );
            // so that it looks like the bearing label
            bankLabel.GetRectTransform().localScale = Vector3.one * 0.5f;
            bankLabel.GetGameObject().SetActive(Plugin.bankIndicatorShowLabel.Value);

            int smallIncrement = InternalState.maxBankAngle <= 10 ? 1 : 5;
            int bigIncrement = InternalState.maxBankAngle <= 10 ? 5 : 15;
            float visualScale = 45f / InternalState.maxBankAngle;

            // now we create the arc with only the increments, with increments as lines, thick ones for big increments and thin ones for small increments
            // they stay fixed with the aircraft, and below the needle that stays fixed with the horizon, so we put them in the same container as the needle but behind it
            for (int i = -InternalState.maxBankAngle; i <= InternalState.maxBankAngle; i += smallIncrement)
            {
                bool isBigIncrement = i % bigIncrement == 0 || i == -InternalState.maxBankAngle ||
                                      i == InternalState.maxBankAngle;
                UIBindings.Draw.UILine line = new(
                    $"i_RBI_increment_{i.ToString()}",
                    new Vector2(0, isBigIncrement ? -radius + 10 : -radius + 5),
                    new Vector2(0, -radius),
                    containerTransform,
                    new Color(0f, 1f, 0f, Plugin.bankIndicatorTransparency.Value),
                    isBigIncrement ? 1.5f : 0.75f,
                    UIBindings.Game.GetFlightHUDFontMaterial(),
                    true,
                    ThemeManager.ThemeContext.HUD,
                    UIBindings.Draw.StyleLabels[ThemeManager.ThemeContext.HUD]["HUD_ImageMainColor"]
                );

                //line.GetRectTransform().transform.RotateAround(containerTransform.position, Vector3.forward, -i * visualScale);
                RotateAroundLocalOrigin(line.GetRectTransform(), -i * visualScale);

                increments.Add(line);
            }
        }

        public void UpdateDisplay(float currentBankAngle)
        {
            if (!containerObject)
                return;
            float clampedBankAngle =
                Mathf.Clamp(currentBankAngle, -InternalState.maxBankAngle, InternalState.maxBankAngle);
            float visualScale = 45f / InternalState.maxBankAngle;
            float angle = -clampedBankAngle * visualScale;
            Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);

            //needle.rectTransform.transform.RotateAround(containerTransform.position, Vector3.forward, -needle.rectTransform.transform.rotation.eulerAngles.z - (clampedBankAngle * visualScale) + containerTransform.rotation.eulerAngles.z);
            needle.rectTransform.localPosition = rotation * needleBasePosition;
            needle.rectTransform.localRotation = rotation * needleBaseRotation;

            if (bankLabel.GetGameObject().activeSelf)
                bankLabel.SetText($"{Mathf.RoundToInt(-currentBankAngle).ToString()}°");
        }

        public void SetPosition(Vector2 position)
        {
            if (containerTransform) containerTransform.anchoredPosition = position;
        }

        public void Destroy()
        {
            if (containerObject)
            {
                Object.Destroy(containerObject);
                containerObject = null;
            }
        }

        private static void RotateAroundLocalOrigin(RectTransform rect, float angle)
        {
            Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            rect.localPosition = rotation * rect.localPosition;
            rect.localRotation = rotation * rect.localRotation;
        }
    }

    // INIT AND REFRESH LOOP
    [HarmonyPatch(typeof(TacScreen), "Initialize")]
    public static class OnPlatformStart
    {
        private static void Postfix()
        {
            try
            {
                LogicEngine.Init();
                DisplayEngine.Init();
            }
            catch (Exception e)
            {
                Plugin.Log($"[BI] Exception: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(TacScreen), "Update")]
    public static class OnPlatformUpdate
    {
        private static void Postfix()
        {
            try
            {
                LogicEngine.Update();
                DisplayEngine.Update();
            }
            catch (Exception e)
            {
                Plugin.Log($"[BI] Exception: {e}");
            }
        }
    }
}