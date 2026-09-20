using System;
using HarmonyLib;
using NO_Tactitools.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NO_Tactitools.UI.HUD;

[HarmonyPatch(typeof(MainMenu), "Start")]
internal class ILSIndicatorPlugin
{
    private static bool _initialized;

    private static void Postfix()
    {
        if (_initialized) return;

        Plugin.Log("[ILS] ILS Screen plugin starting !");
        Plugin.harmony.PatchAll(typeof(ILSIndicatorComponent.OnPlatformStart));
        Plugin.harmony.PatchAll(typeof(ILSIndicatorComponent.OnPlatformUpdate));
        _initialized = true;
        Plugin.Log("[ILS] ILS Screen plugin succesfully started !");
    }
}

internal class ILSIndicatorComponent
{
    public static class LogicEngine
    {
        public static void Init()
        {
            InternalState.ILSWidget?.Destroy();
            InternalState.ILSWidget = null;
            InternalState.isLanding = false;
            InternalState.currentGlideslopeError = 0f;
            InternalState.maxGlideslopeAngle = Plugin.ILSIndicatorMaxAngle.Value;
            InternalState.currentX = Plugin.ILSIndicatorPositionX.Value;
            InternalState.currentY = Plugin.ILSIndicatorPositionY.Value;
            if (InternalState.airbaseOverlayCached == null)
            {
                InternalState.airbaseOverlayCached = GameObject.FindFirstObjectByType<AirbaseOverlay>();
                if (InternalState.airbaseOverlayCached != null)
                    Plugin.Log("[ILS] Cached AirbaseOverlay instance successfully.");
                else
                    Plugin.Log("[ILS] Failed to cache AirbaseOverlay instance.");
            }
        }

        public static void Update()
        {
            if (
                GameBindings.GameState.IsGamePaused()
                || GameBindings.Player.Aircraft.GetAircraft() == null
                || InternalState.airbaseOverlayCached == null)
                return;
            InternalState.isLanding = InternalState.landingCache.GetValue(InternalState.airbaseOverlayCached);
            InternalState.runwayUsage = InternalState.runwayUsageCache.GetValue(InternalState.airbaseOverlayCached);
            InternalState.needsUpdate = InternalState.currentX != Plugin.ILSIndicatorPositionX.Value ||
                                        InternalState.currentY != Plugin.ILSIndicatorPositionY.Value;
            if (InternalState.needsUpdate)
            {
                InternalState.currentX = Plugin.ILSIndicatorPositionX.Value;
                InternalState.currentY = Plugin.ILSIndicatorPositionY.Value;
            }

            if (InternalState.isLanding)
                InternalState.currentGlideslopeError = GetGlideslopeAngleError(
                    GameBindings.Player.Aircraft.GetAircraft(),
                    InternalState.runwayUsage.Runway,
                    InternalState.runwayUsage.Reverse
                );
        }

        public static float GetGlideslopeAngleError(
            Aircraft aircraft,
            Airbase.Runway runway,
            bool reverse
        )
        {
            Vector3 touchdownPos = reverse ? runway.End.position :
                runway.Start != null ? runway.Start.position : runway.End.position;
            Vector3 offset = aircraft.transform.position - touchdownPos;
            Vector3 horizontalOffset = new(offset.x, 0, offset.z);
            float horizontalDistance = horizontalOffset.magnitude;
            if (horizontalDistance < 1f) return 0f;
            float actualHeight = aircraft.transform.position.y - (touchdownPos.y + aircraft.definition.spawnOffset.y);
            float actualAngle = Mathf.Atan2(actualHeight, horizontalDistance) * Mathf.Rad2Deg;
            float targetAngle = Mathf.Atan(0.06f) * Mathf.Rad2Deg;
            return actualAngle - targetAngle;
        }
    }

    public static class InternalState
    {
        public static bool isLanding;
        public static Airbase.Runway.RunwayUsage runwayUsage;
        public static float currentGlideslopeError;
        public static float maxGlideslopeAngle = 1f;
        public static int currentX;
        public static int currentY;
        public static bool needsUpdate;
        public static AirbaseOverlay airbaseOverlayCached;
        public static TraverseCache<AirbaseOverlay, Airbase.Runway.RunwayUsage> runwayUsageCache = new("runwayUsage");
        public static TraverseCache<AirbaseOverlay, bool> landingCache = new("landing");
        public static ILSIndicator ILSWidget;
    }

    public static class DisplayEngine
    {
        public static void Init()
        {
            if (InternalState.ILSWidget == null)
            {
                InternalState.ILSWidget = new ILSIndicator(UIBindings.Game.GetFlightHUDCenterTransform());
                Plugin.Log("[ILS] FLOLS Widget initialized and added to MFD.");
            }

            InternalState.ILSWidget.SetActive(false);
        }

        public static void Update()
        {
            if (
                GameBindings.GameState.IsGamePaused()
                || GameBindings.Player.Aircraft.GetAircraft() == null
                || InternalState.airbaseOverlayCached == null)
                return;
            if (InternalState.isLanding)
            {
                InternalState.ILSWidget.SetActive(true);
                InternalState.ILSWidget.SetBallPosition(InternalState.currentGlideslopeError);
                if (InternalState.needsUpdate)
                    InternalState.ILSWidget.SetPosition(new Vector2(InternalState.currentX, InternalState.currentY));
                if (InternalState.currentGlideslopeError < -0.6f)
                    InternalState.ILSWidget.SetBallColor(Color.red);
                else
                    InternalState.ILSWidget.SetBallColor(Color.yellow);
            }
            else
            {
                InternalState.ILSWidget.SetActive(false);
            }
        }
    }

    // ILS WIDGET PATCH
    public class ILSIndicator
    {
        public UIBindings.Draw.UIRectangle background;
        public UIBindings.Draw.UIRectangle ball;
        public GameObject containerObject;
        public Transform containerTransform;
        public UIBindings.Draw.UIRectangle[] sideBars = new UIBindings.Draw.UIRectangle[4];

        public ILSIndicator(Transform parent)
        {
            containerObject = new GameObject("i_ils_FLOLSContainer");
            containerObject.AddComponent<RectTransform>();
            containerTransform = containerObject.transform;
            containerTransform.SetParent(parent, false);
            containerTransform.localPosition =
                new Vector3(Plugin.ILSIndicatorPositionX.Value, Plugin.ILSIndicatorPositionY.Value, 0);

            background = new UIBindings.Draw.UIAdvancedRectangle(
                "ILS_Background",
                new Vector2(-10, -50),
                new Vector2(10, 50),
                new Color(1, 1, 1, 0.8f),
                0.75f,
                containerTransform,
                Color.clear,
                UIBindings.Game.GetFlightHUDFontMaterial()
            );

            ball = new UIBindings.Draw.UIRectangle(
                "FLOLS_Ball",
                new Vector2(-10, -5),
                new Vector2(10, 5),
                containerTransform,
                new Color(1f, 1f, 0f, 0.8f),
                UIBindings.Game.GetFlightHUDFontMaterial()
            );

            sideBars[0] = new UIBindings.Draw.UIAdvancedRectangle(
                "SideBar_L1",
                new Vector2(-50, -5),
                new Vector2(-35, 5),
                new Color(1, 1, 1, 0.8f),
                1f,
                containerTransform,
                new Color(0f, 1f, 0f, 0.8f),
                UIBindings.Game.GetFlightHUDFontMaterial());

            sideBars[1] = new UIBindings.Draw.UIAdvancedRectangle(
                "SideBar_L2",
                new Vector2(-30, -5),
                new Vector2(-15, 5),
                new Color(1, 1, 1, 0.8f),
                1f,
                containerTransform,
                new Color(0f, 1f, 0f, 0.8f),
                UIBindings.Game.GetFlightHUDFontMaterial());

            sideBars[2] = new UIBindings.Draw.UIAdvancedRectangle(
                "SideBar_R1",
                new Vector2(35, -5),
                new Vector2(50, 5),
                new Color(1, 1, 1, 0.8f),
                1f,
                containerTransform,
                new Color(0f, 1f, 0f, 0.8f),
                UIBindings.Game.GetFlightHUDFontMaterial());

            sideBars[3] = new UIBindings.Draw.UIAdvancedRectangle(
                "SideBar_R2",
                new Vector2(15, -5),
                new Vector2(30, 5),
                new Color(1, 1, 1, 0.8f),
                1f,
                containerTransform,
                new Color(0f, 1f, 0f, 0.8f),
                UIBindings.Game.GetFlightHUDFontMaterial());
        }

        public void SetActive(bool active)
        {
            containerObject?.SetActive(active);
        }

        public void SetBallPosition(float error)
        {
            float yPos = Mathf.Clamp(error * (45f / InternalState.maxGlideslopeAngle), -45f, 45f);
            ball.SetCenter(new Vector2(0, yPos));
        }

        public void SetBallColor(Color color)
        {
            ball.SetColor(color);
        }

        public void SetPosition(Vector2 position)
        {
            if (containerTransform != null) containerTransform.localPosition = new Vector3(position.x, position.y, 0);
        }

        public void Destroy()
        {
            if (containerObject != null)
            {
                Object.Destroy(containerObject);
                containerObject = null;
            }
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
                Plugin.Log($"[ILS] Exception: {e}");
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
                Plugin.Log($"[ILS] Exception: {e}");
            }
        }
    }
}