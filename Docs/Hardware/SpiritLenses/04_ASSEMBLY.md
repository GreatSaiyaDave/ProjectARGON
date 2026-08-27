# Assembly

Dark room. No sunlight on combiners — they can focus it onto the OLED and kill the panel (this is a real warranty exclusion on Rokid/XREAL).

## 0. Safety

- Combiner edges are glass. Tape until pocketed.
- No cracked combiners at eye relief.
- Do not stare at a max-brightness OLED through a combiner at 20 mm for “just a second.”
- One eye on holos, one on the world until you trust IPD.

## 1. First light in the **donor** frame (do this before any print)

1. S23: **Settings → Connected devices → Samsung DeX → Auto start when HDMI connected = ON.**
2. Full-featured USB-C cable, angled plug into the glasses temple, other end into the S23.
3. Put the glasses on. You should see DeX or a desktop. Both eyes.
4. If black: try another cable, another USB-C port (a laptop with DP), the donor cable.
5. Brightness + held ~3 s on many XREAL/Rokid units toggles **3D/SBS**. Leave 2D for this step.
6. Photograph through each combiner (phone camera where your eye goes). Both panels alive?

If only one eye: FPC on the dark side, or a dead OLED. Still harvest; you may need a second donor for the missing engine.

**Do not proceed to printing until at least one engine shows a sharp image.**

## 2. Teardown (Path A)

1. Photograph every screw map.
2. Temples off. USB-C flex is easy to tear — support the connector.
3. Front shell: Rokid Max glues the outer lens; heat + alcohol, do not pry the combiner.
4. Each engine: usually **3× Phillips** plus a glue bead at the diopter seam. Stop when the engine is a sealed brick with an FPC pigtail.
5. DP board: unscrew, keep every FPC labeled L / R with tape.
6. IMU: often UV-glued to metal. Cut glue with a blade; do not flex the MEMS package.
7. Bag parts. Caliper each engine: W, D, H, FPC exit side, combiner rectangle.

Write the six numbers on the bag. Those numbers override SCH-04.

## 3. Open jig (print this first)

A bar with two pockets at **63 mm** IPD, no temples, a 1/4-20 or forehead rest.

1. Drop engines in. Combiner toward the eyes, OLED toward the brow (typical birdbath: panel fires **down**).
2. S23 + donor board still wired on a bench, not on your head.
3. White full-screen on the S23 (or DeX wallpaper).
4. Look through both combiners. You want **one** floating rectangle, not two.
5. If double image: slide IPD ±2 mm, then toe-in 1°. Do not rotate roll; combiners must stay level with each other.
6. When fused: lock pocket screws. That IPD is your frame number.

## 4. Frame

1. CAD pockets = calipers + 0.4 mm. M2 inserts at four corners per engine.
2. USB-C on the **same temple the donor used** (flex length). Strain boot + 15 mm service loop.
3. DP board in the front brow or a temple bay, copper tape to a metal insert for heat (Rokid Max runs hot without a slug).
4. IMU rigid to the **same structure as the engines**. Not on a floppy temple tip.
5. Print PETG. Foam on brow. Strap, not just temples, for the prototype.

## 5. Wire

1. L FPC → L marked board connector. R to R. They are not interchangeable.
2. USB-C receptacle → board. Continuity on VBUS and CC before you plug into the S23.
3. No shorts to the frame. Kapton under the board.
4. Plug into a **cheap USB-C meter** first (5 V present, current &lt; 1.5 A). Then the S23.

## 6. On-head first light

1. Dim room. Strap on. S23 in a pocket, cable up the back of the neck.
2. White rectangle fused?
3. Walk a 2 m line. If the rectangle shears in two, IPD or toe is off — back to the jig, do not “get used to it.”
4. Only then: Unity SBS / Spirit Lenses bring-up (`05_BRINGUP.md`).

## 7. Cosmetics last

Millennium Eye / gold Spirit shell is a **cover** over a jig that already fuses. Do not sculpt the shell until step 6 is boringly repeatable.
