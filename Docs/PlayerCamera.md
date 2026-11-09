# Player camera and movement

Open `Assets/_SnakeCatcher/Scenes/MovementPlayground.unity` and press Play. If that
scene does not exist yet, choose **Snake Catcher > Setup > Create Movement Playground**.
The command creates a reusable player prefab and a separate test scene. Running it
again opens the existing scene without rebuilding your edits.

Click the Game view to capture the cursor. Move the mouse to look around, use WASD
or arrow keys to walk, hold Left Shift to sprint, and press Space to jump. Escape
releases the cursor. Click, Enter, or gamepad Start resumes control.

## Running and jumping

Hold **Left Shift** while moving to run; release it to walk. Press **Space** once
to jump. Holding Space does not repeatedly jump, and pressing it again in the air
does not grant an extra jump. Gamepad bindings use left-stick press for running
and the south face button for jumping.

Select **Player** to adjust the movement settings in the Inspector:

| Setting | Default | Effect |
|---|---:|---|
| Walk Speed | 4 m/s | Normal movement speed. |
| Sprint Speed | 6.5 m/s | Movement speed while holding Sprint. |
| Sprint Enabled | On | Allows running; turn off to use walking only. |
| Jump Height | 1.1 m | Approximate jump height above the takeoff point. |
| Jump Enabled | On | Allows jumping; turn off for a grounded-only controller. |
| Gravity | -20 m/s² | Downward acceleration, including during a jump. |
| Coyote Time | 0.1 s | Grace period for jumping just after walking off an edge. |
| Jump Buffer Time | 0.12 s | Remembers a jump pressed just before landing. |
| Terminal Fall Speed | 50 m/s | Maximum downward speed. |

These are proposed control defaults for playtesting, not PDF requirements.
Diagonal input is limited to the same maximum speed as straight movement. Gravity
continues when the cursor is released, but queued jumps are cleared so returning
to the game cannot trigger an old jump. `SetMovementEnabled(false)` suspends the
entire controller for later vehicle or cutscene integration.

## Camera hierarchy

```text
Player                       CharacterController + FirstPersonPlayerController
  Player Camera              Camera + AudioListener + URP camera data
```

The camera sits 1.6 metres above the player's feet and follows the player through
parenting. Horizontal mouse movement rotates the player around the vertical axis,
so walking forward follows the direction you face. Vertical movement pitches the
camera without tilting the player's collision capsule.

Select **Player** to adjust the controller's **Look** settings in the Inspector:

| Setting | Default | Effect |
|---|---:|---|
| Mouse Sensitivity | 0.12 | Degrees of rotation per mouse pixel. |
| Stick Look Speed | 150 | Degrees per second at full gamepad stick deflection. |
| Pitch Limit | 85 | Maximum up/down angle, preventing a full flip. |
| Invert Y | Off | Reverse vertical camera input. |

Mouse input is a per-frame delta and is not multiplied by delta time. Gamepad
stick input is a turn rate and is multiplied by delta time. The first look sample
after cursor capture is discarded to prevent a recentering jump.

The existing `InputSystem_Actions` asset supplies `Player/Move`, `Look`, `Jump`, and
`Sprint`. The controller creates its own runtime clone so disabling the player
cannot disable input for another system. Avoid adding another mouse-look script
to this camera, since both would rotate the same transforms.

To use the player in a mission, drag
`Assets/_SnakeCatcher/Prefabs/Player/Player.prefab` into the scene, put its feet just
above a floor with a collider, and use its child camera as the scene's active
camera and AudioListener. Keep the player at unit scale.

The first-person perspective and control defaults are technical starting points,
not PDF mission requirements. Head bob and camera shake are not applied.
