using System;
using System.Collections.Generic;
using System.Reflection;
using SnakeCatcher.Player;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Isolated integration probe, run with Pipeline's script run while in Play Mode.
/// Sends real Input System mouse events into the player's private action instance.
/// Invokes the look step directly so Editor focus cannot invalidate the assertions.
/// Every modified pose, action device restriction and virtual device is restored.
/// </summary>
public static class VerifyPlayerCamera
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Enter Play Mode in MovementPlayground first.");

        FirstPersonPlayerController controller = UnityEngine.Object.FindFirstObjectByType<FirstPersonPlayerController>();
        if (controller == null)
            throw new InvalidOperationException("No player controller exists in the open scene.");

        Type type = controller.GetType();
        Transform view = (Transform)type.GetField("viewTransform", Private).GetValue(controller);
        InputActionAsset actions = (InputActionAsset)type.GetField("runtimeInputActions", Private).GetValue(controller);
        MethodInfo look = type.GetMethod("Look", Private);
        MethodInfo synchronize = type.GetMethod("SynchronizeViewRotation", Private);
        FieldInfo discard = type.GetField("discardNextLookInput", Private);
        FieldInfo inverted = type.GetField("invertY", Private);
        float sensitivity = (float)type.GetField("mouseSensitivity", Private).GetValue(controller);
        float limit = (float)type.GetField("pitchLimit", Private).GetValue(controller);
        bool originalEnabled = controller.enabled;
        bool originalInverted = (bool)inverted.GetValue(controller);
        Vector3 originalPosition = controller.transform.position;
        Quaternion originalRotation = controller.transform.rotation;
        Quaternion originalViewRotation = view.localRotation;
        Vector3 originalViewPosition = view.localPosition;
        var originalDevices = actions.devices;
        Mouse mouse = null;
        List<string> passed = new List<string>();

        void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("Camera check failed: " + description);
            passed.Add(description);
        }

        void ResetPose()
        {
            controller.transform.rotation = Quaternion.identity;
            view.localRotation = Quaternion.identity;
            synchronize.Invoke(controller, null);
            discard.SetValue(controller, false);
            inverted.SetValue(controller, false);
        }

        void MouseLook(Vector2 delta)
        {
            // Manual input updates make this a deterministic, isolated input probe;
            // the controller's normal Update is disabled for the duration.
            InputSystem.Update();
            InputSystem.QueueDeltaStateEvent(mouse.delta, delta);
            InputSystem.Update();
            look.Invoke(controller, null);
        }

        try
        {
            controller.enabled = false;
            mouse = InputSystem.AddDevice<Mouse>("SnakeCatcherCameraProbe");
            actions.devices = new InputDevice[] { mouse };
            actions.FindActionMap("Player", true).Enable();

            ResetPose();
            discard.SetValue(controller, true);
            MouseLook(new Vector2(1000f, 1000f));
            Check(Quaternion.Angle(controller.transform.rotation, Quaternion.identity) < 0.01f &&
                  Quaternion.Angle(view.localRotation, Quaternion.identity) < 0.01f,
                "First sample after cursor capture is discarded");

            MouseLook(new Vector2(100f, 0f));
            Check(Mathf.Abs(Mathf.DeltaAngle(0f, controller.transform.eulerAngles.y) - 100f * sensitivity) < 0.05f,
                "Mouse X rotates player yaw at configured sensitivity");

            MouseLook(new Vector2(0f, 20000f));
            Check(Mathf.Abs(Mathf.DeltaAngle(0f, view.localEulerAngles.x) + limit) < 0.05f,
                "Upward pitch stops at the configured limit");
            MouseLook(new Vector2(0f, -20000f));
            Check(Mathf.Abs(Mathf.DeltaAngle(0f, view.localEulerAngles.x) - limit) < 0.05f,
                "Downward pitch stops at the configured limit");
            Check(Mathf.Abs(Mathf.DeltaAngle(0f, controller.transform.eulerAngles.x)) < 0.01f &&
                  Mathf.Abs(Mathf.DeltaAngle(0f, controller.transform.eulerAngles.z)) < 0.01f,
                "Looking vertically leaves the collision body upright");

            ResetPose();
            inverted.SetValue(controller, true);
            MouseLook(new Vector2(0f, 100f));
            Check(Mathf.Abs(Mathf.DeltaAngle(0f, view.localEulerAngles.x) - 100f * sensitivity) < 0.05f,
                "Invert Y reverses vertical mouse look");

            ResetPose();
            Quaternion authoredRotation = Quaternion.Euler(20f, 15f, 7f);
            view.localRotation = authoredRotation;
            synchronize.Invoke(controller, null);
            MouseLook(Vector2.zero);
            Check(Quaternion.Angle(view.localRotation, authoredRotation) < 0.02f,
                "Resynchronizing preserves the camera's authored local pose");

            Vector3 before = view.position;
            Vector3 displacement = new Vector3(2f, 0f, 3f);
            controller.transform.position += displacement;
            Check(Vector3.Distance(view.position - before, displacement) < 0.001f &&
                  Vector3.Distance(view.localPosition, originalViewPosition) < 0.001f,
                "Camera follows the player while keeping its head-height offset");

            return "PASS: " + passed.Count + " camera integration checks\n" + string.Join("\n", passed);
        }
        finally
        {
            actions.Disable();
            actions.devices = originalDevices;
            if (mouse != null)
                InputSystem.RemoveDevice(mouse);
            controller.transform.SetPositionAndRotation(originalPosition, originalRotation);
            view.localPosition = originalViewPosition;
            view.localRotation = originalViewRotation;
            inverted.SetValue(controller, originalInverted);
            synchronize.Invoke(controller, null);
            controller.enabled = originalEnabled;
        }
    }
}
