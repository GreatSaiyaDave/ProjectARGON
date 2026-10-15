#!/usr/bin/env python3
"""Image-embedding helpers (ONNX Runtime) + validation on the wiki anime stills."""
import os, sys, json
import numpy as np
from PIL import Image
import onnxruntime as ort

MODELS = "/tmp/opencode/models"
CH = ["yami_yugi","kaiba","joey","mai","tea","weevil","rex","mako","marik","bakura",
      "bandit_keith","ishizu","odion","pegasus","mokuba","tristan"]

CFG = {
  "clip": dict(path=MODELS+"/clip_vision.onnx", short=224, crop=224,
               mean=[0.48145466,0.4578275,0.40821073], std=[0.26862954,0.26130258,0.27577711]),
  "dino": dict(path=MODELS+"/dinov2.onnx", short=256, crop=224,
               mean=[0.485,0.456,0.406], std=[0.229,0.224,0.225]),
}
_sess = {}

def session(kind):
    if kind not in _sess:
        so = ort.SessionOptions()
        so.intra_op_num_threads = 4
        _sess[kind] = ort.InferenceSession(CFG[kind]["path"], so,
                                           providers=["CPUExecutionProvider"])
    return _sess[kind]

def _prep(im, kind):
    c = CFG[kind]
    im = im.convert("RGBA")
    bg = Image.new("RGBA", im.size, (255, 255, 255, 255))
    bg.alpha_composite(im)
    im = bg.convert("RGB")
    w, h = im.size
    s = c["short"] / min(w, h)
    im = im.resize((max(1, round(w*s)), max(1, round(h*s))), Image.BICUBIC)
    w, h = im.size
    x0, y0 = (w - c["crop"]) // 2, (h - c["crop"]) // 2
    im = im.crop((x0, y0, x0 + c["crop"], y0 + c["crop"]))
    a = np.asarray(im, dtype=np.float32) / 255.0
    a = (a - np.array(c["mean"], np.float32)) / np.array(c["std"], np.float32)
    return a.transpose(2, 0, 1)[None].astype(np.float32)

def embed(im, kind, flip=False):
    if flip:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    s = session(kind)
    x = _prep(im, kind)
    names = [o.name for o in s.get_outputs()]
    outs = s.run(names, {s.get_inputs()[0].name: x})
    v = None
    for n, o in zip(names, outs):
        if o.ndim == 2:
            v = o[0]; break
    if v is None:
        o = outs[0]
        v = o[0, 0] if o.ndim == 3 else o.reshape(-1)
    v = np.asarray(v, np.float32)
    return v / (np.linalg.norm(v) + 1e-9)

if __name__ == "__main__":
    for k in CFG:
        s = session(k)
        print(k, "in", [i.name + str(i.shape) for i in s.get_inputs()],
              "out", [o.name + str(o.shape) for o in s.get_outputs()])
