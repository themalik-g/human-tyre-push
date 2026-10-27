# Testing guide - from nothing to playing

## 1. Install Unity (about 20 min, free Personal licence)
1. Go to **unity.com/download**, install **Unity Hub**, create or sign in to a Unity account.
2. Get the editor version this project uses: **6000.1.17f1** (any 6000.1.x also works).
   Open **unity.com/releases/editor/archive**, find 6000.1.17f1, click **Install** (it opens Unity Hub).
   A newer 6000.x editor also works, but Unity will offer to upgrade the project; use the exact version for a clean test.
3. In the module list, tick your platform's **Build Support** only if you want to make a build later
   (Windows: "Windows Build Support (Mono)"). Not needed just to test in the editor.
4. Activate the free Personal licence when Hub asks.

## 2. Open the project
1. Unzip the project **fully** (do not open from inside the zip). Use a short path, e.g. `C:\Projects\hanman_tyre_push`.
2. Unity Hub > **Projects > Add > Add project from disk** > select the folder that contains `Assets`, `Packages`, `ProjectSettings`.
3. Click it to open with 6000.1.17f1. First import takes 5-15 minutes (large textures). Do not close it.
4. If a dialog says **"Enter Safe Mode"**, there is a compile error: click *Ignore*, copy the red Console lines and send them to me.
5. If a **TMP Essentials** or **Input System** prompt appears, accept it.
6. When import finishes, the Console should show: `Tyre Push: auto-setup complete...`
   If not: menu **Tools > Tyre Push > Run Setup On Prototype Scene**.

## 3. Run it
1. In the Project window open `Assets/Scenes/TyrePushPrototype.unity`.
2. Press **Play**. Click once inside the Game view (locks the mouse). **Esc** frees the mouse.
3. Optional: set the Game view to *Full HD (1920x1080)* so the HUD matches the reference screenshots.

## 4. Test checklist (expected result in brackets)
| # | Do this | Expected |
|---|---|---|
| 1 | Press Play, do nothing | Tyre stands 2 m ahead on a narrow sandy track over empty space; hollow black tyre with tread, no rim. Character holds a stick. HUD: BALANCE (bottom left), SPEED (bottom right), IN RANGE badge |
| 2 | WASD, mouse, Shift, Space | Character moves, camera orbits, run is faster, jump is about 1 m |
| 3 | Walk to the tyre | IN RANGE badge turns green when the stick can reach it |
| 4 | Tap LMB on the resting tyre | A firm kick: the tyre lurches off (clear start from rest), small puff and knock sound. SPEED rises to about 10 to 14 km/h |
| 5 | Leave it alone | It slows and stops by itself in about 6 s (loses momentum) |
| 6 | Run behind it and tap LMB in rhythm | Speed builds with the rhythm of strokes; faster strokes = faster tyre |
| 7 | Hit it slightly off the centre line (move the camera a little left or right of the tyre while stroking) | A small drift to one side |
| 8 | Hit it far off-centre (camera well to one side) | A big deflection; the tyre veers strongly off the straight line |
| 9 | Hold RMB while it rolls | Progressive braking: the tyre slows smoothly (not an instant stop), the stick squeals |
| 10 | Tilt the tyre, hold RMB | The stick presses the upper tread and helps it back towards upright (only if not too far gone) |
| 11 | Push it into a hazard barrier | It bounces off and loses energy or stops; its path changes |
| 12 | Let a fast tyre bounce back into you | You are thrown back and stumble for a moment |
| 13 | Push it past the edge of the track (or walk off) | RUN ENDED screen with the reason. Press R to restart |
| 14 | Press R during a run | Tyre is stood up in front of you |
| 15 | Controller: sticks, L3, A, RT, LT | Same as keyboard; RT and LT are analog; controller rumbles on a hard stroke |
| 16 | Compare SPEED bar to the tyre | Bar matches real motion; km/h text shown |

## 5. Tune while playing
Select **Player** (stick forces, jump, speed) and **Tyre** (mass, friction, rolling resistance) in the Hierarchy and change values live.
Values changed in Play mode are lost when you stop; note the good ones and type them in afterwards.
Full table in `README.md`.

## 6. Common problems
| Symptom | Fix |
|---|---|
| Red compile errors in Console | Copy them to me; fastest fix |
| Capsule instead of the X Bot, or plain procedural tyre | Models not linked: run **Tools > Tyre Push > Run Setup On Prototype Scene**, then Play again |
| Pink materials | Wrong render pipeline material: select the material > Shader > Standard |
| Tyre drifts too easily / too hard to steer | `StickTool.aimAssist` (higher = easier) |
| Tyre falls over instantly | Raise `defaultWidth` or `friction` on the Tyre object; check Fixed Timestep is 0.01 (Project Settings > Time) |
| Tyre won't roll fast | Check Project Settings > Physics > Default Max Angular Speed is 100 |
| Hand not on stick | Animator needs IK Pass: **Tools > Tyre Push > Prepare Player Animator** |
| No sound | Game view "Mute Audio" off; Camera has an Audio Listener (created automatically) |
| Controller ignored | Click Game view first; check Edit > Project Settings > Player > Active Input Handling = *Both* |
| Mouse stuck/invisible | Press Esc |

## 7. Make a build
File > Build Profiles (Build Settings) > pick Windows/Mac/Linux > the prototype scene is already listed > **Build**.
(Needs the matching Build Support module installed in Unity Hub.)
