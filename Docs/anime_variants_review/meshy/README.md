# Meshy reconstructions (image → 3D)

Meshy turns a character's art into a mesh we can rotate — the answer to "look 2D, but exist in 3D".
`multi-image-to-3d` takes 1–4 images of the same subject and accepts **base64 data URIs**, so no
public hosting is needed.

## Recipe (reproducible)

```bash
# inputs: the DA front render, normalised to a 1024² white canvas, figure ~980 px tall,
#         plus a back reference at the same scale
python3 - <<'PY'
import base64, json
d=lambda p: "data:image/png;base64,"+base64.b64encode(open(p,"rb").read()).decode()
json.dump({"image_urls":[d("front.png"), d("back.png")], "ai_model":"meshy-7.1",
           "should_texture":True, "enable_pbr":False, "target_formats":["glb"],
           "symmetry_mode":"auto", "image_enhancement":True}, open("req.json","w"))
PY
curl -X POST https://api.meshy.ai/openapi/v1/multi-image-to-3d \
  -H "Authorization: Bearer $MESHY_KEY" -H 'Content-Type: application/json' -d @req.json
# {"result":"<task-id>"} → poll GET /openapi/v1/multi-image-to-3d/<task-id> until SUCCEEDED
```

Cost 30 credits (meshy-7.1, 2K texture) of the 3009 the owner has. Task
`01a14140-bb3c-7676-a52f-49185aa79cf7` (Yugi) took ~75 s; the GLB is 21 MB, base colour 2K.

## Turnarounds

`render_turnaround.py` (Blender headless) imports the GLB and renders four yaw angles **twice**: as
delivered (PBR) and flattened to the painted base colour — the cel look the game targets. Measure it
against the art rather than trusting the eye:

| view | silhouette aspect | mean RGB |
|---|---|---|
| art front (`yugi_da_front`) | 2.87 | 88, 82, 104 |
| Meshy flat front | 3.24 | 97, 101, 121 |
| Meshy flat back | 3.29 | **149, 144, 153** |

Silhouette IoU (art front vs Meshy flat front, height-normalised): **0.681**.

## What the first run proved

- The pipeline works end to end: art → task → GLB → turnaround, 30 credits, no hosting.
- The **front carries**: IoU 0.68, colour within ~10 levels of the art.
- The **back does not**: mean 149,144,153 is a pale neutral, nowhere near the dark uniform the front
  shows — Meshy had nothing to work from. The back input was a 143×540 anime cutout (aspect 3.78, a
  sliver), so the geometry was guessed and the rear texture fell back to neutral.

**Conclusion: the back view is the whole game.** The artist publishes none — all 45 Duelist Kingdom
items in the gallery are front-facing (checked title and description on every one) — so a back has to
come from the anime (`anime_backviews/`, the Season 1 scan) or from a render the owner finds.
