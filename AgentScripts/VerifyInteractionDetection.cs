using System;
using System.Collections.Generic;
using System.Reflection;
using SnakeCatcher.Interaction;
using UnityEngine;

/// <summary>
/// Synchronous Play Mode integration probe for camera-based interaction geometry.
/// It uses the real PlayerInteractor with a temporary view far from the level.
/// No engine frames, UI operations, input events or permanent assets are needed.
/// </summary>
public static class VerifyInteractionDetection
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Enter MovementPlayground Play Mode before running this probe.");

        PlayerInteractor interactor = UnityEngine.Object.FindFirstObjectByType<PlayerInteractor>();
        if (interactor == null)
            throw new InvalidOperationException("No PlayerInteractor exists in the open scene.");

        Type type = interactor.GetType();
        FieldInfo Field(string name) => type.GetField(name, Private)
            ?? throw new MissingFieldException(type.Name, name);
        var savedFields = new Dictionary<FieldInfo, object>();
        void Override(string name, object value)
        {
            FieldInfo field = Field(name);
            if (!savedFields.ContainsKey(field)) savedFields.Add(field, field.GetValue(interactor));
            field.SetValue(interactor, value);
        }

        var objects = new List<GameObject>();
        var passed = new List<string>();
        var skipped = new List<string>();
        CursorLockMode originalCursorLock = Cursor.lockState;
        bool originalCursorVisible = Cursor.visible;
        bool originalEnabled = interactor.enabled;
        Vector3 origin = new Vector3(100f, 10f, 100f);

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Interaction check failed: " + message);
            passed.Add(message);
        }

        GameObject Box(string name, float distance, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Interaction Probe " + name;
            box.transform.SetPositionAndRotation(origin + Vector3.forward * distance, Quaternion.identity);
            box.transform.localScale = size;
            box.GetComponent<Renderer>().enabled = false;
            objects.Add(box);
            return box;
        }

        InteractionPracticeTarget Target(string name, float distance)
        {
            GameObject box = Box(name, distance, Vector3.one * 0.5f);
            return box.AddComponent<InteractionPracticeTarget>();
        }

        Interactable Scan()
        {
            Physics.SyncTransforms();
            return interactor.ScanForTarget();
        }

        void Park(GameObject value)
        {
            value.transform.position = origin + Vector3.right * 20f;
            Physics.SyncTransforms();
        }

        try
        {
            var viewObject = new GameObject("Interaction Probe View");
            objects.Add(viewObject);
            viewObject.transform.SetPositionAndRotation(origin, Quaternion.identity);
            Override("viewTransform", viewObject.transform);
            Override("interactionRange", 3f);
            Override("interactionMask", (LayerMask)~0);
            Check((QueryTriggerInteraction)Field("triggerInteraction").GetValue(interactor) == QueryTriggerInteraction.Ignore,
                "Trigger colliders are ignored by the configured default");
            Override("triggerInteraction", QueryTriggerInteraction.Ignore);

            InteractionPracticeTarget target = Target("center target", 2f);
            Check(Scan() == target, "A visible center target inside the interaction range is detected");

            target.transform.position = origin + Vector3.forward * 4f;
            Check(Scan() == null, "A target beyond the maximum interaction range is rejected");
            target.transform.position = origin + Vector3.forward * 2f;
            GameObject wall = Box("solid wall", 1f, new Vector3(1f, 1f, 0.2f));
            Check(Scan() == null, "A solid wall blocks interaction with the target behind it");
            Park(wall);

            target.transform.position = origin + Vector3.forward * 1.25f;
            InteractionPracticeTarget behind = Target("target behind disabled object", 2.2f);
            target.enabled = false;
            Check(Scan() == null, "A disabled target is rejected and its solid collider still blocks targets behind it");
            Park(target.gameObject);
            Park(behind.gameObject);

            var compound = new GameObject("Interaction Probe Compound Target");
            objects.Add(compound);
            compound.transform.SetPositionAndRotation(origin + Vector3.forward * 2f, Quaternion.identity);
            GameObject compoundChild = Box("compound child collider", 2f, Vector3.one * 0.5f);
            compoundChild.transform.SetParent(compound.transform, true);
            InteractionPracticeTarget compoundTarget = compound.AddComponent<InteractionPracticeTarget>();
            Check(Scan() == compoundTarget, "A collider on a child resolves its parent Interactable");

            GameObject ownCollider = Box("own child collider", 0.45f, Vector3.one * 0.25f);
            ownCollider.transform.SetParent(interactor.transform, true);
            Check(Scan() == compoundTarget, "The player's own child collider is ignored");

            GameObject trigger = Box("trigger in front", 0.9f, Vector3.one * 0.3f);
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            Check(Scan() == compoundTarget, "An unrelated trigger does not hide the target behind it");

            // TryInteract is input-gated. Temporarily provide its focus state and
            // locked cursor while preserving the real controller's normal state.
            FieldInfo focus = type.GetField("hasFocus", Private);
            if (focus != null) Override("hasFocus", true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            int countBefore = compoundTarget.InteractionCount;
            bool activated = interactor.TryInteract();
            if (activated)
            {
                Check(compoundTarget.InteractionCount == countBefore + 1,
                    "TryInteract invokes the currently visible target exactly once");

                // Seed a visible target, then make it stale without waiting for
                // Update. Each operation must perform its own fresh geometry scan.
                Check(Scan() == compoundTarget, "The target is visible before the stale-target checks");
                compound.transform.position = origin + Vector3.forward * 4f;
                Physics.SyncTransforms();
                int countAfterActivation = compoundTarget.InteractionCount;
                Check(!interactor.TryInteract() && compoundTarget.InteractionCount == countAfterActivation,
                    "TryInteract rejects a previously visible target after it moves out of range");

                compound.transform.position = origin + Vector3.forward * 2f;
                Check(Scan() == compoundTarget, "The target is visible again before inserting an obstruction");
                wall.transform.position = origin + Vector3.forward;
                Physics.SyncTransforms();
                Check(!interactor.TryInteract() && compoundTarget.InteractionCount == countAfterActivation,
                    "TryInteract rejects a previously visible target after a solid wall occludes it");
            }
            else
            {
                skipped.Add("TryInteract execution and stale-target tests: gameplay input gate is closed in this Editor session.");
            }

            return "PASS: " + passed.Count + " interaction integration checks\n" +
                string.Join("\n", passed) + (skipped.Count == 0 ? "" : "\nSKIPPED:\n" + string.Join("\n", skipped));
        }
        finally
        {
            foreach (var saved in savedFields) saved.Key.SetValue(interactor, saved.Value);
            // Destroy children before their parents. Unity's fake-null semantics
            // make this safe for both compound colliders and player-owned probes.
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            interactor.enabled = originalEnabled;
            Cursor.lockState = originalCursorLock;
            Cursor.visible = originalCursorVisible;
            Physics.SyncTransforms();
        }
    }
}
