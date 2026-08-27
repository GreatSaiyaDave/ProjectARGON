# Full-body clothing layers

Paper-doll overlays composited over `body_full.png`. Same pose and canvas (720×1280). Isolated on chroma green (runtime keyed).

| File | Slot | Pockets |
|------|------|--------:|
| body_full.png | mannequin | — |
| look_0 … look_7 | face + hair (not a clothing slot) | — |
| hat_cap / hat_beanie | hat | 0 |
| face_goggles / face_visor | facial | 0 |
| shirt_plain | shirt (starter) | 0 |
| shirt_hoodie | shirt | 2 |
| shirt_cargo | shirt | 3 |
| shirt_duelcoat | shirt | 4 |
| hands_gloves / hands_gauntlets | hands | 0 |
| bottoms_plain | bottoms (starter) | 1 |
| bottoms_duel | bottoms | 1 |
| bottoms_cargo | bottoms | 2 |
| shoes_plain / shoes_kicks / shoes_boots | shoes | 0 |

Empty slots (`hat_none`, `face_none`, `hands_none`) have no sprite.

Draw order (back → front): body → shirt/coat → bottoms → hands → shoes → look → facial → hat.

- **Look under hat/facial.** Cap, beanie, goggles, and visor overlay the portrait. Those sprites must be accessory-only: transparent where the face and hair show. A full faceless mannequin head in the hat slot covers `look_n` and deletes the face.
- **Bottoms over shirt.** Pants overlap a long coat’s opening (and hide shorts if a coat still includes them). Coat tails stay visible beside the legs.
- **Hands over bottoms.** Glove sprites must be gloves only — baked shorts sit on top of the pants.
- **Shoes over bottoms.** Pants should not include feet.

Original un-punched Imagine sheets live in `_bak/`.
