using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using SnakeCatcher.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Play Mode integration probe for the real controller and CharacterController.
/// Normal Update is disabled; Move(true) runs exactly once per rendered frame.
/// Input events use the normal Dynamic Input System update, never manual updates.
/// This file lives outside Assets and is executed through Pipeline script run.
/// </summary>
public static class VerifyRunningAndJumping
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task<string> Run()
    {
        if (!Application.isPlaying || Time.timeScale <= 0f)
            throw new InvalidOperationException("Run in an unpaused MovementPlayground Play Mode session.");

        var controller = UnityEngine.Object.FindFirstObjectByType<FirstPersonPlayerController>();
        if (controller == null)
            throw new InvalidOperationException("No FirstPersonPlayerController exists in this scene.");

        Type type = controller.GetType();
        FieldInfo Field(string name) => type.GetField(name, Private)
            ?? throw new MissingFieldException(type.Name, name);
        MethodInfo move = type.GetMethod("Move", Private);
        MethodInfo clear = type.GetMethod("ClearTransientInputState", Private);
        if (move == null || clear == null)
            throw new InvalidOperationException("Controller movement methods are missing.");

        var body = controller.GetComponent<CharacterController>();
        var view = (Transform)Field("viewTransform").GetValue(controller);
        var actions = (InputActionAsset)Field("runtimeInputActions").GetValue(controller);
        if (actions == null || body == null || view == null)
            throw new InvalidOperationException("Player has not initialized its required references.");

        bool originalEnabled = controller.enabled;
        bool originalBodyEnabled = body.enabled;
        Vector3 originalPosition = controller.transform.position;
        Quaternion originalRotation = controller.transform.rotation;
        Vector3 originalViewPosition = view.localPosition;
        Quaternion originalViewRotation = view.localRotation;
        var originalDevices = actions.devices;
        CursorLockMode originalCursorLock = Cursor.lockState;
        bool originalCursorVisible = Cursor.visible;
        var enabledActions = new List<InputAction>();
        foreach (InputAction action in actions)
            if (action.enabled) enabledActions.Add(action);

        var savedFields = new Dictionary<FieldInfo, object>();
        string[] fieldsToRestore =
        {
            "verticalVelocity", "lastGroundedTime", "lastJumpPressedTime",
            "sprintEnabled", "jumpEnabled", "<IsSprinting>k__BackingField",
            "pitch", "viewYaw", "viewRoll", "hasFocus", "discardNextLookInput",
            "wantsCursorLocked", "movementEnabled"
        };
        foreach (string name in fieldsToRestore)
        {
            FieldInfo field = type.GetField(name, Private);
            if (field != null) savedFields.Add(field, field.GetValue(controller));
        }

        float walkingSpeed = (float)Field("walkSpeed").GetValue(controller);
        float runningSpeed = (float)Field("sprintSpeed").GetValue(controller);
        float jumpHeight = (float)Field("jumpHeight").GetValue(controller);
        float gravity = (float)Field("gravity").GetValue(controller);
        float terminalSpeed = (float)Field("terminalFallSpeed").GetValue(controller);
        float coyoteTime = (float)Field("coyoteTime").GetValue(controller);
        float bufferTime = (float)Field("jumpBufferTime").GetValue(controller);
        FieldInfo vertical = Field("verticalVelocity");
        Keyboard keyboard = null;
        var temporaryObjects = new List<GameObject>();
        var passed = new List<string>();
        var skipped = new List<string>();
        float startedAt = Time.realtimeSinceStartup;
        float maximumDelta = 0f;
        Vector3 openFloor = new Vector3(8f, 0.05f, -9f);

        void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("Movement check failed: " + description);
            passed.Add(description);
        }

        float VerticalSpeed() => (float)vertical.GetValue(controller);

        async Task<float> Step(params Key[] heldKeys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(heldKeys));
            int previousFrame = Time.frameCount;
            do
            {
                await Task.Yield();
                if (!Application.isPlaying || controller == null)
                    throw new InvalidOperationException("Play Mode ended during movement verification.");
                if (Time.realtimeSinceStartup - startedAt > 18f)
                    throw new TimeoutException("Movement probe exceeded 18 real seconds; inspect Editor frame progress.");
            } while (Time.frameCount == previousFrame);

            float delta = Time.deltaTime;
            if (delta <= 0f)
                throw new InvalidOperationException("The game stopped advancing positive simulation time.");
            maximumDelta = Mathf.Max(maximumDelta, delta);
            move.Invoke(controller, new object[] { true });
            return delta;
        }

        async Task Advance(float duration, params Key[] heldKeys)
        {
            float elapsed = 0f;
            while (elapsed < duration) elapsed += await Step(heldKeys);
        }

        void Teleport(Vector3 position)
        {
            body.enabled = false;
            controller.transform.SetPositionAndRotation(position, Quaternion.identity);
            body.enabled = true;
            vertical.SetValue(controller, 0f);
            clear.Invoke(controller, null);
            Physics.SyncTransforms();
        }

        async Task ResetOnFloor()
        {
            Teleport(openFloor);
            await Advance(0.2f);
            if (!body.isGrounded)
                throw new InvalidOperationException("Open-floor probe position has no walkable floor.");
        }

        async Task<float> MeasureSpeed(float duration, params Key[] heldKeys)
        {
            await ResetOnFloor();
            Vector3 start = controller.transform.position;
            float elapsed = 0f;
            while (elapsed < duration) elapsed += await Step(heldKeys);
            Vector3 displacement = controller.transform.position - start;
            displacement.y = 0f;
            return displacement.magnitude / elapsed;
        }

        GameObject ProbeBox(string name, Vector3 position, Vector3 scale)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetPositionAndRotation(position, Quaternion.identity);
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().enabled = false;
            temporaryObjects.Add(box);
            Physics.SyncTransforms();
            return box;
        }

        try
        {
            controller.enabled = false;
            keyboard = InputSystem.AddDevice<Keyboard>("SnakeCatcherMovementProbe");
            actions.devices = new InputDevice[] { keyboard };
            actions.FindActionMap("Player", true).Enable();
            Field("sprintEnabled").SetValue(controller, true);
            Field("jumpEnabled").SetValue(controller, true);

            float walk = await MeasureSpeed(0.25f, Key.W);
            Check(Mathf.Abs(walk - walkingSpeed) < 0.08f,
                "Walking speed matches configuration: " + walk.ToString("F2") + " m/s");
            float sprint = await MeasureSpeed(0.25f, Key.W, Key.LeftShift);
            Check(Mathf.Abs(sprint - runningSpeed) < 0.08f && sprint > walk,
                "Sprint speed matches configuration: " + sprint.ToString("F2") + " m/s");
            float diagonal = await MeasureSpeed(0.25f, Key.W, Key.D, Key.LeftShift);
            Check(Mathf.Abs(diagonal - runningSpeed) < 0.08f,
                "Diagonal sprint is normalized: " + diagonal.ToString("F2") + " m/s");
            Field("sprintEnabled").SetValue(controller, false);
            float disabledSprint = await MeasureSpeed(0.15f, Key.W, Key.LeftShift);
            Check(Mathf.Abs(disabledSprint - walkingSpeed) < 0.08f,
                "Disabling sprint keeps Shift movement at walking speed");
            Field("sprintEnabled").SetValue(controller, true);

            await ResetOnFloor();
            float groundY = controller.transform.position.y;
            Field("jumpEnabled").SetValue(controller, false);
            await Step(Key.Space);
            Check(VerticalSpeed() <= 0f && controller.transform.position.y < groundY + 0.05f,
                "Disabling jumping prevents a Space jump");
            Field("jumpEnabled").SetValue(controller, true);
            await Step();

            await ResetOnFloor();
            groundY = controller.transform.position.y;
            float flightTime = await Step(Key.Space);
            float apex = controller.transform.position.y;
            Check(VerticalSpeed() > 0f && apex > groundY,
                "A grounded Space press launches upward");
            flightTime += await Step();
            apex = Mathf.Max(apex, controller.transform.position.y);
            float beforeSecondPress = VerticalSpeed();
            flightTime += await Step(Key.Space);
            apex = Mathf.Max(apex, controller.transform.position.y);
            Check(VerticalSpeed() < beforeSecondPress,
                "Pressing Space again in the air does not reset upward velocity");
            while (flightTime < 1.35f)
            {
                flightTime += await Step(Key.Space);
                apex = Mathf.Max(apex, controller.transform.position.y);
            }
            float measuredJumpHeight = apex - groundY;
            float discreteTolerance = Mathf.Max(0.2f, Mathf.Sqrt(-2f * gravity * jumpHeight) * maximumDelta * 1.5f);
            Check(measuredJumpHeight > jumpHeight - discreteTolerance && measuredJumpHeight < jumpHeight + 0.1f,
                "Jump apex approximates configured height: " + measuredJumpHeight.ToString("F2") + " m");
            Check(body.isGrounded && Mathf.Abs(controller.transform.position.y - groundY) < 0.06f,
                "The player lands and holding Space does not automatically jump again");

            if (bufferTime > 0f)
            {
                Teleport(new Vector3(openFloor.x, groundY + 0.5f, openFloor.z));
                while (!body.isGrounded && controller.transform.position.y - groundY > 0.18f)
                    await Step();
                if (body.isGrounded)
                    skipped.Add("Jump buffer: a slow frame crossed the entire landing window.");
                else
                {
                    await Step(Key.Space);
                    bool bufferedLaunch = VerticalSpeed() > 0f;
                    float elapsed = 0f;
                    while (!bufferedLaunch && elapsed < bufferTime + 0.15f)
                    {
                        elapsed += await Step();
                        bufferedLaunch = VerticalSpeed() > 0f;
                    }
                    Check(bufferedLaunch, "A Space press shortly before landing is buffered into one jump");
                }
            }

            if (coyoteTime > 0f)
            {
                GameObject ledge = ProbeBox("Movement Probe Ledge", new Vector3(8f, 3.8f, -9f), new Vector3(1f, 0.2f, 1f));
                Teleport(new Vector3(8f, 3.94f, -9f));
                await Advance(0.2f);
                Check(body.isGrounded, "Coyote test starts grounded on a temporary ledge");
                float leftLedgeAt = Time.time;
                body.Move(new Vector3(1.2f, 0f, 0f));
                await Step(Key.Space);
                if (Time.time - leftLedgeAt <= coyoteTime)
                    Check(VerticalSpeed() > 0f, "Jumping just after leaving a ledge uses coyote time");
                else
                    skipped.Add("Coyote time: the next rendered frame exceeded the configured grace window.");
                UnityEngine.Object.DestroyImmediate(ledge);
            }

            Teleport(new Vector3(8f, 200f, -9f));
            vertical.SetValue(controller, -terminalSpeed * 3f);
            await Step();
            Check(Mathf.Abs(VerticalSpeed() + terminalSpeed) < 0.01f,
                "Falling speed is clamped to terminalFallSpeed");

            GameObject ceiling = ProbeBox("Movement Probe Ceiling", new Vector3(8f, 2.35f, -9f), new Vector3(4f, 0.3f, 4f));
            await ResetOnFloor();
            bool ceilingHit = false;
            bool ceilingCancelledAscent = false;
            float ceilingTime = await Step(Key.Space);
            while (ceilingTime < 0.8f)
            {
                if ((body.collisionFlags & CollisionFlags.Above) != 0)
                {
                    ceilingHit = true;
                    ceilingCancelledAscent |= VerticalSpeed() <= 0f;
                }
                ceilingTime += await Step();
            }
            Check(ceilingHit && ceilingCancelledAscent,
                "Hitting a low ceiling cancels upward velocity");
            Check(body.isGrounded, "The player lands after hitting a low ceiling");
            UnityEngine.Object.DestroyImmediate(ceiling);

            return "PASS: " + passed.Count + " running/jumping integration checks in " +
                (Time.realtimeSinceStartup - startedAt).ToString("F1") + " s\n" +
                string.Join("\n", passed) + (skipped.Count == 0 ? "" : "\nSKIPPED:\n" + string.Join("\n", skipped));
        }
        finally
        {
            actions.Disable();
            actions.devices = originalDevices;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            foreach (GameObject temporary in temporaryObjects)
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);

            if (controller != null && body != null)
            {
                body.enabled = false;
                controller.transform.SetPositionAndRotation(originalPosition, originalRotation);
                view.localPosition = originalViewPosition;
                view.localRotation = originalViewRotation;
                body.enabled = originalBodyEnabled;
                controller.enabled = originalEnabled;
                foreach (var saved in savedFields) saved.Key.SetValue(controller, saved.Value);
                actions.Disable();
                foreach (InputAction action in enabledActions) action.Enable();
                Cursor.lockState = originalCursorLock;
                Cursor.visible = originalCursorVisible;
                Physics.SyncTransforms();
            }
        }
    }
}
