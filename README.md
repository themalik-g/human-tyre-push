# Tyre Push - Unity project (client prototype)

Unity **6000.1.x** (built with 6000.1.17f1), built-in render pipeline, Input System package.
Steps to open and test: see `TESTING_GUIDE.md`.

## Quick start
1. Open the project in Unity. Wait for the first import, then for the Console line `Tyre Push: auto-setup complete`
   (it links the X Bot, stick and animator into the scene once). If it does not appear: **Tools > Tyre Push > Run Setup On Prototype Scene**.
2. Open `Assets/Scenes/TyrePushPrototype.unity` and press **Play**.

## What the client asked for, and how it is built
| Client requirement | Implementation |
|---|---|
| Simple temporary models, no visible cubes/capsules | X Bot character, wooden stick, generated tyre |
| Tyre with hollow centre and tread, **no rim** | Generated round tyre: hollow centre, rounded shoulders, 4 circumferential grooves + slanted lateral grooves. (`Use Project Tyre Model` on the bootstrap switches to the project's own mesh) |
| Stick contact applies force **and torque** to the tyre Rigidbody | Every contact is `AddForceAtPosition` at the real contact point: this gives the Rigidbody a push **and** the torque (offset x force) in one call |
| Rhythm / timing: speed depends on stroke frequency | A tap is a stroke (about 0.28 s, impulse). Each stroke adds up to about 4 m/s; the tyre then slows by itself, so speed is kept up by repeating strokes in rhythm |
| Tyre loses momentum and angular velocity if not pushed | Rolling resistance (dirt track value 0.06), air drag and angular drag. About 6 s to coast to rest after a stroke |
| Slightly off-centre = small deflection, far off-centre = large deflection | The stick lands where you aim (camera direction, partly helped towards the tyre by `Aim Assist`). It pushes along its own axis, limited by the friction cone. A hit near the centre line gives a small yaw/lean; a hit at the shoulder or sidewall gives a large one |
| Steer the tyre, keep it on the narrow track | The player adjusts aim and position; RMB brace presses the upper tread with a limited anti-lean reflex |
| Obstacles absorb energy / change path / stop the tyre | Real collisions with low-bounce barriers; the test track has three |
| Tyre bounces back and knocks the player back / interrupts stride | A fast tyre hitting the player exchanges momentum (restitution 0.35): the player is thrown back and stumbles for 0.2 to 0.7 s (slow acceleration, no sprint) |
| In range check | The IN RANGE badge turns green when the tyre is within stick reach and in front |
| SPEED meter = forward movement speed | SPEED bar and km/h read the Rigidbody's speed along the ground |
| BALANCE bar = left/right lean | Reads the real lean angle of the tyre (degrees shown) |
| If tyre or player falls off the track the run ends | Below `Fall Y` a RUN ENDED screen appears. R / Y restarts |

Start from rest: the first contact on a resting tyre adds a short breakaway kick (about 120 N decaying in 0.12 s) and force builds at a limited rate, so the tyre lurches off like a real heavy tyre instead of moving instantly.
Brake: RMB presses the upper tread; the force builds progressively (analog on LT), friction does the braking (about 5 to 6 m/s^2 at most), and the stick squeals on the rubber.

## Controls
| | Keyboard / mouse | Controller |
|---|---|---|
| Move | WASD | Left stick |
| Camera | Mouse | Right stick |
| Run | Shift | L3 |
| Jump | Space | A / Cross |
| Push / poke | LMB (tap = stroke, hold = push) | RT / R2 (analog) |
| Brace / brake | RMB | LT / L2 |
| Reset tyre in front of you, or restart after RUN ENDED (extra) | R | Y / Triangle |
| Unlock cursor (extra) | Esc | |

## Tuning (Inspector, live in Play mode)
| Want | Change |
|---|---|
| Harder / easier to steer | `StickTool.aimAssist` (lower = more manual aim and more deflection) |
| Stronger / weaker stroke | `pokeForce`, `pokeReach`, `pokeDuration` |
| Steady push | `holdForce`, `servoGain`, `pushOverspeed` |
| Breakaway kick | `launchSurge`, `launchSurgeTime`, `pushRiseRate` |
| Brake strength / feel | `braceForce`, `braceFriction`, `braceRiseRate` |
| Tyre loses speed faster / slower | `TyrePhysics.rollingResistance` (0.015 asphalt, 0.06 dirt, 0.1 sand), `angularDrag` |
| Tyre tips over easier / harder | `TyrePhysics.defaultWidth`, `friction`, `mass` |
| Knockback strength | `PlayerMotor.knockbackRestitution`, `knockbackMaxSpeed` |
| Where the run ends | `TyrePushBootstrap.fallY` |
| Character speed / jump | `PlayerMotor.walkSpeed`, `runSpeed`, `jumpHeight` |

## For the client's own level
Open his level, **Tools > Tyre Push > Add Bootstrap To Open Scene**, untick **Build Test Arena**, set **Fall Y** a little below the lowest part of the track. Level meshes need colliders.

## Project settings changed
Fixed timestep 0.01 s; default max angular speed 7 to 100 rad/s (the old value stops a tyre from rolling); animator `xbot animtor` has IK Pass enabled so the hand holds the stick.

## Honest status
This was written without being able to run Unity, so it has **not been compiled or play-tested in the editor**. The force model was checked in a numerical simulation, the generated mesh and distance maths were checked numerically, and all files were checked for structure and cross-references, but the feel values (forces, friction, aim assist) are tuned starting points that need a short pass in Play mode. If Unity shows a compile error, send the Console text.
