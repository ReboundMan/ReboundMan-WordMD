"""Generate WordMD icon: stethoscope over a document with 'MD' text.
Outputs Assets/AppIcon.ico (multi-resolution) and PNG previews.
"""
from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

OUT_DIR = Path(__file__).resolve().parents[1] / "src" / "WordMD" / "Assets"
OUT_DIR.mkdir(parents=True, exist_ok=True)

DOC_COLOR     = (255, 255, 255)
DOC_OUTLINE   = (0,  78, 212)
DOC_FOLD      = (235, 240, 250)
TEXT_COLOR    = (0,  78, 212)
LINE_COLOR    = (180, 200, 230)
SCOPE_COLOR   = (35, 50, 75)
SCOPE_HILITE  = (90, 110, 150)
EARPIECE      = (220, 70, 70)
BG_TRANSPARENT = (0, 0, 0, 0)


def draw_icon(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), BG_TRANSPARENT)
    d = ImageDraw.Draw(img)
    s = size

    pad = int(s * 0.10)
    doc_x0, doc_y0 = pad, pad
    doc_x1, doc_y1 = s - pad, s - pad
    fold = int(s * 0.22)

    shadow_off = max(1, int(s * 0.015))
    sh = Image.new("RGBA", (s, s), BG_TRANSPARENT)
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle(
        [doc_x0 + shadow_off, doc_y0 + shadow_off, doc_x1 + shadow_off, doc_y1 + shadow_off],
        radius=max(2, int(s * 0.06)), fill=(0, 0, 0, 60),
    )
    img = Image.alpha_composite(img, sh)
    d = ImageDraw.Draw(img)

    body = [
        (doc_x0, doc_y0),
        (doc_x1 - fold, doc_y0),
        (doc_x1, doc_y0 + fold),
        (doc_x1, doc_y1),
        (doc_x0, doc_y1),
    ]
    d.polygon(body, fill=DOC_COLOR, outline=DOC_OUTLINE)
    fold_tri = [(doc_x1 - fold, doc_y0), (doc_x1 - fold, doc_y0 + fold), (doc_x1, doc_y0 + fold)]
    d.polygon(fold_tri, fill=DOC_FOLD, outline=DOC_OUTLINE)

    if s >= 48:
        line_w = max(1, int(s * 0.015))
        line_x0 = doc_x0 + int(s * 0.10)
        line_x1 = doc_x1 - int(s * 0.10)
        for i, y_frac in enumerate([0.46, 0.54, 0.62, 0.70, 0.78]):
            y = int(s * y_frac)
            x_end = line_x1 - (int(s * 0.10) if i == 4 else 0)
            d.line([(line_x0, y), (x_end, y)], fill=LINE_COLOR, width=line_w)

    try:
        font = ImageFont.truetype("segoeuib.ttf", max(8, int(s * 0.26)))
    except Exception:
        font = ImageFont.load_default()
    text = "MD"
    bbox = d.textbbox((0, 0), text, font=font)
    tw = bbox[2] - bbox[0]
    tx = (s - tw) // 2 - bbox[0]
    ty = int(s * 0.30) - bbox[1]
    d.text((tx, ty), text, font=font, fill=TEXT_COLOR)

    stroke = max(2, int(s * 0.045))
    ep_r = max(2, int(s * 0.05))
    ep1 = (int(s * 0.20), int(s * 0.20))
    ep2 = (int(s * 0.34), int(s * 0.20))
    junction = (int(s * 0.27), int(s * 0.46))
    d.line([ep1, junction], fill=SCOPE_COLOR, width=stroke)
    d.line([ep2, junction], fill=SCOPE_COLOR, width=stroke)
    d.ellipse([ep1[0] - ep_r, ep1[1] - ep_r, ep1[0] + ep_r, ep1[1] + ep_r],
              fill=EARPIECE, outline=SCOPE_COLOR)
    d.ellipse([ep2[0] - ep_r, ep2[1] - ep_r, ep2[0] + ep_r, ep2[1] + ep_r],
              fill=EARPIECE, outline=SCOPE_COLOR)

    chest = (int(s * 0.74), int(s * 0.78))
    pts = []
    steps = 30
    for i in range(steps + 1):
        t = i / steps
        cx = int(s * 0.32)
        cy = int(s * 0.85)
        x = int((1 - t) ** 2 * junction[0] + 2 * (1 - t) * t * cx + t * t * chest[0])
        y = int((1 - t) ** 2 * junction[1] + 2 * (1 - t) * t * cy + t * t * chest[1])
        pts.append((x, y))
    for i in range(len(pts) - 1):
        d.line([pts[i], pts[i + 1]], fill=SCOPE_COLOR, width=stroke)

    cp_r = max(4, int(s * 0.13))
    d.ellipse([chest[0] - cp_r, chest[1] - cp_r, chest[0] + cp_r, chest[1] + cp_r],
              fill=SCOPE_COLOR, outline=SCOPE_HILITE)
    inner_r = int(cp_r * 0.55)
    d.ellipse([chest[0] - inner_r, chest[1] - inner_r, chest[0] + inner_r, chest[1] + inner_r],
              fill=SCOPE_HILITE)

    return img


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    # Pillow's ICO writer downsamples a single source image to the requested sizes.
    # Provide a high-res master and let it generate all entries in one file.
    master = draw_icon(256)
    ico_path = OUT_DIR / "AppIcon.ico"
    master.save(ico_path, format="ICO", sizes=[(s, s) for s in sizes])
    # Also drop a couple of PNG previews for docs / store listings
    master.save(OUT_DIR / "AppIcon-256.png")
    master.resize((48, 48), Image.LANCZOS).save(OUT_DIR / "AppIcon-48.png")
    print(f"Wrote {ico_path}")


if __name__ == "__main__":
    main()
