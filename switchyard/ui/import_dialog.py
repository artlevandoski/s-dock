"""Preview of a folder scan before anything is written to the library."""
from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QAbstractItemView,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
)

from ..db import CONTENT_KIND_LABELS, GENERATION_LABELS, GENERATIONS
from ..scanner import ScanResult, apply_folder_generation

COL_INCLUDE, COL_TITLE, COL_GEN, COL_KIND, COL_TID, COL_NOTES, COL_FILE = range(7)


class ImportPreviewDialog(QDialog):
    def __init__(self, result: ScanResult, folder_generation: str = "unclassified", parent=None):
        super().__init__(parent)
        self.setWindowTitle("Import Preview")
        self.resize(1100, 560)
        self.result = result
        self.folder_generation = folder_generation
        self._updating = False

        layout = QVBoxLayout(self)
        header = QLabel(
            f"<b>{len(result.entries)}</b> game file(s) found in <b>{result.folder}</b>. "
            "Nothing is imported until you click Import. Your files are never moved or renamed."
        )
        header.setWordWrap(True)
        layout.addWidget(header)

        row = QHBoxLayout()
        row.addWidget(QLabel("Assign generation to this folder:"))
        self.folder_gen_combo = QComboBox()
        for g in GENERATIONS:
            self.folder_gen_combo.addItem(GENERATION_LABELS[g], g)
        self.folder_gen_combo.setCurrentIndex(GENERATIONS.index(folder_generation))
        self.folder_gen_combo.currentIndexChanged.connect(self._folder_generation_changed)
        row.addWidget(self.folder_gen_combo)
        row.addStretch(1)
        layout.addLayout(row)

        self.table = QTableWidget(len(result.entries), 7)
        self.table.setHorizontalHeaderLabels(["Import", "Title", "Generation", "Type", "Title ID", "Review notes", "File"])
        self.table.verticalHeader().setVisible(False)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        hh = self.table.horizontalHeader()
        for c in (COL_INCLUDE, COL_GEN, COL_KIND, COL_TID):
            hh.setSectionResizeMode(c, QHeaderView.ResizeMode.ResizeToContents)
        hh.setSectionResizeMode(COL_TITLE, QHeaderView.ResizeMode.Interactive)
        hh.setSectionResizeMode(COL_NOTES, QHeaderView.ResizeMode.Interactive)
        hh.setSectionResizeMode(COL_FILE, QHeaderView.ResizeMode.Stretch)
        self.table.setColumnWidth(COL_TITLE, 300)
        self.table.setColumnWidth(COL_NOTES, 230)
        self.table.setWordWrap(False)
        self.table.itemChanged.connect(self._item_changed)
        layout.addWidget(self.table, 1)

        self.summary = QLabel()
        self.summary.setWordWrap(True)
        layout.addWidget(self.summary)

        if result.missing_known or result.errors:
            lines = []
            if result.missing_known:
                lines.append(
                    f"<b>{len(result.missing_known)}</b> previously imported file(s) under this folder are missing "
                    "and will be shown as FILE MISSING in the library:"
                )
                lines += [f"&nbsp;&nbsp;{p}" for p in result.missing_known[:8]]
                if len(result.missing_known) > 8:
                    lines.append("&nbsp;&nbsp;…")
            if result.errors:
                lines.append(f"<b>{len(result.errors)}</b> location(s) could not be read:")
                lines += [f"&nbsp;&nbsp;{e}" for e in result.errors[:5]]
            warn = QLabel("<br>".join(lines))
            warn.setStyleSheet("color: #d9822b;")
            warn.setWordWrap(True)
            warn.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
            layout.addWidget(warn)

        buttons = QDialogButtonBox()
        self.import_button = buttons.addButton("Import", QDialogButtonBox.ButtonRole.AcceptRole)
        buttons.addButton(QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

        self._populate()

    # ----------------------------------------------------------------- building
    def _populate(self) -> None:
        self._updating = True
        for r, e in enumerate(self.result.entries):
            inc = QTableWidgetItem()
            flags = Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable
            if not e.in_library:
                flags |= Qt.ItemFlag.ItemIsUserCheckable
            inc.setFlags(flags)
            inc.setCheckState(Qt.CheckState.Checked if e.include else Qt.CheckState.Unchecked)
            self.table.setItem(r, COL_INCLUDE, inc)

            title = QTableWidgetItem(e.title)
            title.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable | Qt.ItemFlag.ItemIsEditable)
            title.setToolTip("Double-click to correct the title")
            self.table.setItem(r, COL_TITLE, title)

            combo = QComboBox()
            for g in GENERATIONS:
                combo.addItem(GENERATION_LABELS[g], g)
            combo.setEnabled(not e.in_library)
            combo.currentIndexChanged.connect(lambda _i, row=r: self._row_generation_changed(row))
            self.table.setCellWidget(r, COL_GEN, combo)

            for col, text in (
                (COL_KIND, CONTENT_KIND_LABELS.get(e.kind, e.kind)),
                (COL_TID, e.title_id or ""),
                (COL_FILE, e.path),
            ):
                item = QTableWidgetItem(text)
                item.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
                if col == COL_FILE:
                    item.setToolTip(e.path)
                self.table.setItem(r, col, item)
            notes = QTableWidgetItem()
            notes.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable)
            self.table.setItem(r, COL_NOTES, notes)
        self._updating = False
        self._refresh_rows()

    def _refresh_rows(self) -> None:
        self._updating = True
        for r, e in enumerate(self.result.entries):
            combo: QComboBox = self.table.cellWidget(r, COL_GEN)
            combo.blockSignals(True)
            combo.setCurrentIndex(GENERATIONS.index(e.generation))
            combo.blockSignals(False)
            notes = self.table.item(r, COL_NOTES)
            notes.setText(e.notes())
            flagged = bool(e.flags - {"in_library"})
            notes.setForeground(Qt.GlobalColor.darkYellow if flagged else Qt.GlobalColor.gray)
        self._updating = False
        self._update_summary()

    def _update_summary(self) -> None:
        entries = self.result.entries
        to_import = sum(1 for e in entries if e.include and not e.in_library)
        present = sum(1 for e in entries if e.in_library)
        flagged = sum(1 for e in entries if not e.in_library and (e.flags - {"in_library"}))
        unclassified = sum(1 for e in entries if e.include and not e.in_library and e.generation == "unclassified")
        text = f"<b>{to_import}</b> to import · {present} already in library · {flagged} flagged for review"
        if unclassified:
            text += f" · {unclassified} will be imported as Unclassified"
        self.summary.setText(text)
        self.import_button.setEnabled(to_import > 0 or present > 0)

    # ------------------------------------------------------------------ events
    def _folder_generation_changed(self) -> None:
        self.folder_generation = self.folder_gen_combo.currentData()
        apply_folder_generation([e for e in self.result.entries if not e.in_library], self.folder_generation)
        self._refresh_rows()

    def _row_generation_changed(self, row: int) -> None:
        e = self.result.entries[row]
        e.generation = self.table.cellWidget(row, COL_GEN).currentData()
        e.generation_source = "manual"
        e.flags.discard("generation_conflict")
        self._refresh_rows()

    def _item_changed(self, item: QTableWidgetItem) -> None:
        if self._updating:
            return
        e = self.result.entries[item.row()]
        if item.column() == COL_INCLUDE and not e.in_library:
            e.include = item.checkState() == Qt.CheckState.Checked
            self._update_summary()
        elif item.column() == COL_TITLE:
            text = item.text().strip()
            if text:
                e.title = text
