# 生成 WeeklyReport 应用图标（Fluent 风格：蓝色圆角方块 + 白色周报文档 + 绿色对勾徽章）。
# 输出多尺寸 .ico（大尺寸用精细稿，小尺寸用粗笔画简化稿），以及预览 PNG。
# 用法：python scripts/generate-icon.py

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "WeeklyReport.App" / "Assets"
PREVIEW = Path(__file__).resolve().parent.parent / "artifacts" / "icon-preview"

BLUE_TOP = (71, 160, 241)
BLUE_BOTTOM = (14, 95, 184)
HEADER_BLUE = (27, 94, 173)
LINE_BLUE = (184, 212, 240)
WHITE = (255, 255, 255)
GREEN = (35, 164, 85)


def vertical_gradient_rounded_square(size: int, radius: int, top, bottom) -> Image.Image:
    """垂直渐变的圆角方块。"""
    gradient = Image.new("RGB", (1, size))
    for y in range(size):
        ratio = y / max(size - 1, 1)
        gradient.putpixel(
            (0, y),
            tuple(round(top[i] + (bottom[i] - top[i]) * ratio) for i in range(3)),
        )
    gradient = gradient.resize((size, size))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    image.paste(gradient, (0, 0), mask)
    return image


def draw_detailed(size: int) -> Image.Image:
    """精细稿：用于 64/128/256。"""
    canvas = size / 1024.0
    image = vertical_gradient_rounded_square(size, round(228 * canvas), BLUE_TOP, BLUE_BOTTOM)
    draw = ImageDraw.Draw(image)

    # 白色周报文档
    sheet = (232 * canvas, 196 * canvas, 828 * canvas, 846 * canvas)
    draw.rounded_rectangle(sheet, radius=round(36 * canvas), fill=WHITE)

    # 文档顶部的蓝色标题条（上圆角、下方直角）
    header = (sheet[0], sheet[1], sheet[2], sheet[1] + 128 * canvas)
    draw.rounded_rectangle(header, radius=round(36 * canvas), fill=HEADER_BLUE)
    draw.rectangle((header[0], header[1] + 60 * canvas, header[2], header[3]), fill=HEADER_BLUE)

    # 正文行（长短交错的三条胶囊线）
    line_left = sheet[0] + 60 * canvas
    line_right = sheet[2] - 60 * canvas
    widths = [0.95, 0.74, 0.48]
    y = header[3] + 96 * canvas
    for ratio in widths:
        thickness = round(40 * canvas)
        end = line_left + (line_right - line_left) * ratio
        draw.rounded_rectangle(
            (line_left, y - thickness / 2, end, y + thickness / 2),
            radius=thickness // 2,
            fill=LINE_BLUE,
        )
        y += 136 * canvas

    # 右下角绿色对勾徽章（明显突出文档边缘）
    cx, cy, r = 724 * canvas, 786 * canvas, 168 * canvas
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=GREEN)
    check = [
        (cx - 70 * canvas, cy + 2 * canvas),
        (cx - 9 * canvas, cy + 62 * canvas),
        (cx + 68 * canvas, cy - 57 * canvas),
    ]
    draw.line(check, fill=WHITE, width=round(52 * canvas), joint="curve")
    for point in check:
        px, py = point
        spot = round(26 * canvas)
        draw.ellipse((px - spot, py - spot, px + spot, py + spot), fill=WHITE)
    return image


def draw_simple(size: int) -> Image.Image:
    """粗笔画简化稿：用于 16/24/32/48，小尺寸下保持可辨识。"""
    canvas = size / 512.0
    image = vertical_gradient_rounded_square(size, round(118 * canvas), BLUE_TOP, BLUE_BOTTOM)
    draw = ImageDraw.Draw(image)

    sheet = (118 * canvas, 104 * canvas, 428 * canvas, 470 * canvas)
    draw.rounded_rectangle(sheet, radius=round(22 * canvas), fill=WHITE)

    header = (sheet[0], sheet[1], sheet[2], sheet[1] + 84 * canvas)
    draw.rounded_rectangle(header, radius=round(22 * canvas), fill=HEADER_BLUE)
    draw.rectangle((header[0], header[1] + 40 * canvas, header[2], header[3]), fill=HEADER_BLUE)

    line_left = sheet[0] + 40 * canvas
    line_right = sheet[2] - 40 * canvas
    widths = [0.92, 0.60]
    y = header[3] + 96 * canvas
    for ratio in widths:
        thickness = round(42 * canvas)
        end = line_left + (line_right - line_left) * ratio
        draw.rounded_rectangle(
            (line_left, y - thickness / 2, end, y + thickness / 2),
            radius=thickness // 2,
            fill=LINE_BLUE,
        )
        y += 130 * canvas

    cx, cy, r = 372 * canvas, 390 * canvas, 132 * canvas
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=GREEN)
    check = [
        (cx - 56 * canvas, cy + 2 * canvas),
        (cx - 9 * canvas, cy + 50 * canvas),
        (cx + 54 * canvas, cy - 46 * canvas),
    ]
    draw.line(check, fill=WHITE, width=round(44 * canvas), joint="curve")
    for point in check:
        px, py = point
        spot = round(22 * canvas)
        draw.ellipse((px - spot, py - spot, px + spot, py + spot), fill=WHITE)
    return image


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    PREVIEW.mkdir(parents=True, exist_ok=True)

    detailed = {s: draw_detailed(s) for s in (256, 128, 64)}
    simple = {s: draw_simple(s) for s in (48, 32, 24, 16)}

    frames = [detailed[256], *simple.values(), detailed[128], detailed[64]]
    sizes = [(256, 256), (48, 48), (32, 32), (24, 24), (16, 16), (128, 128), (64, 64)]
    icon_path = ASSETS / "app.ico"
    detailed[256].save(
        icon_path,
        format="ICO",
        sizes=sizes,
        append_images=frames[1:],
    )

    detailed[256].save(PREVIEW / "app-256.png")
    simple[32].resize((128, 128), Image.NEAREST).save(PREVIEW / "app-32-nearest.png")
    simple[48].save(PREVIEW / "app-48.png")
    print(f"written: {icon_path}")
    stored = sorted(Image.open(icon_path).info.get("sizes", []))
    print(f"stored sizes: {stored}")


if __name__ == "__main__":
    main()
