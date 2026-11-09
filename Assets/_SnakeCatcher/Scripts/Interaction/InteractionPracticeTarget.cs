using UnityEngine;

namespace SnakeCatcher.Interaction
{
    /// <summary>A harmless playground target for testing detection and the Interact action.</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPracticeTarget : Interactable
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Color inactiveColor = new Color(0.95f, 0.57f, 0.13f);
        [SerializeField] private Color activeColor = new Color(0.15f, 0.80f, 0.40f);

        private MaterialPropertyBlock propertyBlock;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public bool IsActivated { get; private set; }
        public int InteractionCount { get; private set; }
        public override string Prompt => IsActivated
            ? "Hold E to turn the practice marker off"
            : "Hold E to turn the practice marker on";

        private void OnEnable()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();
            UpdateColor();
        }

        public override void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
                return;

            IsActivated = !IsActivated;
            InteractionCount++;
            UpdateColor();
            Debug.Log("Practice marker " + (IsActivated ? "on" : "off") + ". Interaction detected.", this);
        }

        private void UpdateColor()
        {
            if (targetRenderer == null)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColor, IsActivated ? activeColor : inactiveColor);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
