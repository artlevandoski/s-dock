"""Generated placeholder covers (no third-party artwork is bundled)."""
from __future__ import annotations

from PySide6.QtCore import QRectF, QSize, Qt
from PySide6.QtGui import QColor, QFont, QIcon, QLinearGradient, QPainter, QPainterPath, QPixmap

from ..db import GENERATION_LABELS

GEN_COLORS = {
    "switch1": QColor("#a3343c"),
    "switch2": QColor("#2f58a3"),
    "unclassified": QColor("#56606d"),
}

_cache: dict[tuple, QPixmap] = {}


def placeholder_cover(title: str, generation: str, size: QSize, missing: bool = False) -> QPixmap:
    key = (title, generation, size.width(), size.height(), missing)
    if key in _cache:
        return _cache[key]
    w, h = size.width(), size.height()
    pm = QPixmap(w, h)
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    p.setRenderHint(QPainter.RenderHint.TextAntialiasing)

    rect = QRectF(0.5, 0.5, w - 1, h - 1)
    shape = QPainterPath()
    shape.addRoundedRect(rect, w * 0.06, w * 0.06)
    p.setClipPath(shape)

    base = GEN_COLORS.get(generation, GEN_COLORS["unclassified"])
    if missing:
        base = QColor("#3a3d42")
    grad = QLinearGradient(0, 0, 0, h)
    grad.setColorAt(0.0, base.lighter(135))
    grad.setColorAt(1.0, base.darker(170))
    p.fillPath(shape, grad)

    band_h = h * 0.13
    band = QRectF(0, h - band_h, w, band_h)
    p.fillRect(band, QColor(0, 0, 0, 110))

    font = QFont()
    font.setBold(True)
    font.setPixelSize(max(9, int(h * 0.05)))
    p.setFont(font)
    p.setPen(QColor(255, 255, 255, 220))
    p.drawText(band, Qt.AlignmentFlag.AlignCenter, GENERATION_LABELS.get(generation, "Unclassified").upper())

    font.setPixelSize(max(10, int(h * 0.075)))
    p.setFont(font)
    p.setPen(QColor("white"))
    text_rect = QRectF(w * 0.08, h * 0.08, w * 0.84, h - band_h - h * 0.12)
    p.drawText(text_rect, int(Qt.AlignmentFlag.AlignCenter | Qt.TextFlag.TextWordWrap), title)

    if missing:
        badge_h = h * 0.11
        badge = QRectF(w * 0.12, h * 0.06, w * 0.76, badge_h)
        path = QPainterPath()
        path.addRoundedRect(badge, badge_h / 2, badge_h / 2)
        p.fillPath(path, QColor("#d9822b"))
        font.setPixelSize(max(9, int(badge_h * 0.5)))
        p.setFont(font)
        p.setPen(QColor("black"))
        p.drawText(badge, Qt.AlignmentFlag.AlignCenter, "FILE MISSING")

    p.setClipping(False)
    p.setPen(QColor(255, 255, 255, 40))
    p.drawPath(shape)
    p.end()
    _cache[key] = pm
    return pm


def app_icon() -> QIcon:
    icon = QIcon()
    for s in (16, 32, 48, 64, 128, 256):
        pm = QPixmap(s, s)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        path = QPainterPath()
        path.addRoundedRect(QRectF(0, 0, s, s), s * 0.2, s * 0.2)
        grad = QLinearGradient(0, 0, s, s)
        grad.setColorAt(0, QColor("#2f58a3"))
        grad.setColorAt(1, QColor("#a3343c"))
        p.fillPath(path, grad)
        pen = p.pen()
        pen.setColor(QColor("white"))
        pen.setWidthF(max(1.0, s * 0.07))
        pen.setCapStyle(Qt.PenCapStyle.RoundCap)
        p.setPen(pen)
        # Two rails and a switch track: a small "yard" mark.
        p.drawLine(int(s * 0.25), int(s * 0.2), int(s * 0.25), int(s * 0.8))
        p.drawLine(int(s * 0.75), int(s * 0.2), int(s * 0.75), int(s * 0.8))
        p.drawLine(int(s * 0.25), int(s * 0.65), int(s * 0.75), int(s * 0.35))
        p.end()
        icon.addPixmap(pm)
    return icon
