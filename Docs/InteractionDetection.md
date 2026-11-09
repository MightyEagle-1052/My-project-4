# Interaction detection

In the movement playground, look at the orange practice marker in front of the
spawn point and **hold E** to turn it green. Release E and hold again to switch it
back. The existing Input System asset uses a Hold interaction; a quick tap does
not activate the object.

The setup menu is **Snake Catcher > Setup > Add Interaction Detection**. It updates
the reusable player prefab and adds the practice target without rebuilding the
playground. Running it again keeps existing marker and scene edits.

`PlayerInteractor` casts a ray from the center of the player's camera. The default
maximum range is **3 metres**, measured to the collider surface. The first solid
object blocks the ray, so an item behind a wall cannot be selected. Trigger
colliders and the player's own colliders are ignored. Child colliders can point
to an `Interactable` component on their parent.

Keep the **Interaction Mask** inclusive of walls and other objects that should
block the ray. Restricting it to interactable objects alone would allow the ray
to pass through excluded walls.

Hold input belongs to the target selected when the hold started. Looking away,
changing targets, leaving range, releasing the cursor, disabling movement, losing
focus, or pausing cancels that attempt. Release the button before trying again.
Range and visibility are checked again when the action executes.

Select the Player's **Player Interactor** component to tune its range and collision
mask. `CurrentTarget` and `CurrentPrompt`, plus target-change and interaction
events, are available for a future HUD. This step does not add a HUD or inventory.

To make a mission object usable, derive its script from
`SnakeCatcher.Interaction.Interactable`, provide its `Prompt`, and implement
`Interact(PlayerInteractor)`. Override `CanInteract` for conditions such as an
already-secured gas can or a bag that is full. Put a solid collider on the object
or one of its children. Keep the practice marker as a test object, separate from
mission-specific capture and safety rules.

The 3-metre range and hold behavior are technical defaults for playtesting, not
requirements verified against the project PDF.
