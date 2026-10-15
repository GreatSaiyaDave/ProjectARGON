#!/usr/bin/env python3
"""
Give every 2D anime character sprite a 3D form: derive a DEPTH map and a NORMAL map
from the flat standing art, so AnimeCelShader can light the actual body (per-pixel
terminator) and parallax it with the camera. No ink outlines.

Method (deterministic, no ML):
  depth  = normalised euclidean distance transform of the alpha silhouette, blurred
           (a smooth rounded "relief" of the body: wide torso is deep, thin arms shallow)
  normal = tangent-space normal from the depth gradient (Sobel), amplitude A
  packed = <id>_normal.png  RGBA: rgb = normal*0.5+0.5, a = depth (0..255)

Writes Assets/Resources/WRLDZ/AnimeCharacters/<id>/<id>_normal.png (+ .meta) and a
review preview sheet under research/anime_variants/normals/.
"""
import os, sys, hashlib
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt, gaussian_filter

HERE = "/home/greatsaiyadave/argon-avatar/research"
OUT = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
PREVIEW = os.path.join(HERE, "anime_variants", "normals")

NORMAL_SIZE = 512          # normal/depth map resolution (sprite is 1024)
DEPTH_BLUR = 3.0
DEPTH_GAMMA = 0.85
AMPLITUDE = 30.0           # depth-gradient -> normal slope exaggeration

sys.path.insert(0, HERE)


def meta_text(guid):
    """TextureImporter meta for a LINEAR (non-sRGB) default texture (normal maps must not
    be gamma-decoded)."""
    return f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 0
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
    bumpmap:
      convertToNormalMap: 0
      externalNormalMap: 0
      heightScale: 0.25
      normalMapFilter: 0
      flipGreenChannel: 0
    isReadable: 0
    streamingMipmaps: 0
    streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 1024
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 512
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:"""


def guid_for(name):
    return hashlib.md5(name.encode("utf-8")).hexdigest()[:32]


def depth_and_normal(rgba):
    """rgba: HxWx4 uint8 (the sprite). Returns (normal_rgba uint8, depth float 0..1)."""
    a = rgba[:, :, 3]
    m = a > 16
    if not m.any():
        raise SystemExit("empty alpha")
    dist = distance_transform_edt(m).astype(np.float32)
    dmax = dist.max() or 1.0
    d = dist / dmax
    d = gaussian_filter(d, DEPTH_BLUR)
    d = np.clip(d, 0, 1) ** DEPTH_GAMMA

    g_row, g_col = np.gradient(d)                     # per-pixel slopes
    nx = -g_col * AMPLITUDE
    ny = g_row * AMPLITUDE
    nz = np.ones_like(d)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx * inv, ny * inv, nz * inv

    rgb = np.stack([(nx * 0.5 + 0.5), (ny * 0.5 + 0.5), (nz * 0.5 + 0.5)], axis=-1)
    rgb = np.clip(rgb * 255.0, 0, 255)
    alpha = np.clip(d * 255.0, 0, 255)
    out = np.dstack([rgb, alpha]).astype(np.uint8)
    out[~m] = (128, 128, 255, 0)                      # flat + no depth outside the figure
    return out, d


def shade_preview(sprite_rgba, normal_rgba, lx, ly, lz):
    """Tiny stand-in for AnimeShade so a human can judge the form. Light (lx,ly,lz) in image
    space (x right, y up, z toward viewer)."""
    n = normal_rgba[:, :, :3].astype(np.float32) / 255.0 * 2.0 - 1.0
    l = np.array([lx, ly, lz], np.float32); l /= np.linalg.norm(l)
    ndl = n[:, :, 0] * l[0] + n[:, :, 1] * l[1] + n[:, :, 2] * l[2]
    lit = (ndl > 0.15).astype(np.float32)
    col = sprite_rgba[:, :, :3].astype(np.float32)
    shadow = col * np.array([0.80, 0.78, 0.92], np.float32) * 0.85
    out = col * lit[:, :, None] + shadow * (1 - lit)[:, :, None]
    a = sprite_rgba[:, :, 3:4].astype(np.float32)
    return np.clip(out, 0, 255).astype(np.uint8), a


def main():
    ids = sorted(d for d in os.listdir(OUT) if os.path.isdir(os.path.join(OUT, d)))
    os.makedirs(PREVIEW, exist_ok=True)
    for cid in ids:
        front = os.path.join(OUT, cid, f"{cid}_front.png")
        if not os.path.exists(front):
            continue
        img = Image.open(front).convert("RGBA")
        rgba = np.array(img)
        normal, depth = depth_and_normal(rgba)

        nimg = Image.fromarray(normal).resize((NORMAL_SIZE, NORMAL_SIZE), Image.LANCZOS)
        np_path = os.path.join(OUT, cid, f"{cid}_normal.png")
        nimg.save(np_path, optimize=True)
        meta = np_path + ".meta"
        if not os.path.exists(meta):
            open(meta, "w").write(meta_text(guid_for(f"{cid}/{cid}_normal.png")))

        # preview: original | normal map | lit left | lit right | lit top
        tiles = [rgba]
        tiles.append(normal)
        for lx, ly, lz in ((-0.7, 0.3, 0.6), (0.7, 0.3, 0.6), (0.0, 0.9, 0.4)):
            rgb, al = shade_preview(rgba, normal, lx, ly, lz)
            tiles.append(np.dstack([rgb, al]))
        tile = 384
        sheet = Image.new("RGB", (tile * len(tiles), tile), (24, 24, 30))
        for i, t in enumerate(tiles):
            im = Image.fromarray(t.astype(np.uint8) if t.dtype != np.uint8 else t).convert("RGBA")
            im.thumbnail((tile - 8, tile - 8))
            bg = Image.new("RGBA", im.size, (60, 60, 72, 255)); bg.alpha_composite(im)
            sheet.paste(bg.convert("RGB"), (i * tile + 4, 4))
        sheet.save(os.path.join(PREVIEW, cid + ".jpg"), quality=88)
        print(f"{cid:13} normal/depth written  {nimg.size}  depth max {depth.max():.2f}")
    print("previews:", PREVIEW)


if __name__ == "__main__":
    main()
