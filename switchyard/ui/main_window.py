"""Main window: cover grid, details panel, Add Folder, Emulator Settings, Play."""
from __future__ import annotations

import os

from PySide6.QtCore import QModelIndex, QSize, Qt, QTimer, QUrl
from PySide6.QtGui import QAction, QDesktopServices, QIcon, QKeySequence, QStandardItem, QStandardItemModel
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QComboBox,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QLabel,
    QListView,
    QMainWindow,
    QMessageBox,
    QPushButton,
    QSizePolicy,
    QSplitter,
    QStackedWidget,
    QToolBar,
    QVBoxLayout,
    QWidget,
)

from .. import paths
from ..db import (
    CONTENT_KIND_LABELS,
    GENERATION_LABELS,
    GENERATIONS,
    PROFILE_GENERATION_LABELS,
    Edition,
    EmulatorProfile,
    Library,
    path_key,
)
from ..launcher import LaunchController, validate_launch
from ..scanner import import_entries, scan_folder
from .covers import placeholder_cover
from .import_dialog import ImportPreviewDialog
from .profile_dialog import TEST_TAG, ProfileDialog

EDITION_ID_ROLE = Qt.ItemDataRole.UserRole + 1
GRID_COVER = QSize(150, 240)
DETAIL_COVER = QSize(200, 320)
LAST_SELECTED_KEY = "last_selected_edition"


class MainWindow(QMainWindow):
    def __init__(self, library: Library, parent: QWidget | None = None):
        super().__init__(parent)
        self.library = library
        self.setWindowTitle("Switch Yard")
        self.resize(1180, 760)
        self._editions: dict[str, Edition] = {}
        self._running_edition: str | None = None

        self.launcher = LaunchController(log_path=str(paths.log_dir() / "last_launch.log"), parent=self)
        self.launcher.launched.connect(self._on_launched)
        self.launcher.finished.connect(self._on_finished)
        self.launcher.failed.connect(self._on_failed)

        self._build_actions()
        self._build_ui()
        self.refresh(select_id=self.library.get_setting(LAST_SELECTED_KEY))

    # =================================================================== build
    def _build_actions(self) -> None:
        self.add_folder_action = QAction("Add Folder…", self)
        self.add_folder_action.setShortcut(QKeySequence("Ctrl+O"))
        self.add_folder_action.triggered.connect(self.add_folder)
        self.settings_action = QAction("Emulator Settings…", self)
        self.settings_action.setShortcut(QKeySequence("Ctrl+E"))
        self.settings_action.triggered.connect(self.open_emulator_settings)
        self.data_folder_action = QAction("Open Data Folder", self)
        self.data_folder_action.triggered.connect(
            lambda: QDesktopServices.openUrl(QUrl.fromLocalFile(str(paths.data_dir())))
        )
        quit_action = QAction("Quit", self)
        quit_action.setShortcut(QKeySequence.StandardKey.Quit)
        quit_action.triggered.connect(self.close)

        file_menu = self.menuBar().addMenu("&Library")
        file_menu.addAction(self.add_folder_action)
        file_menu.addSeparator()
        file_menu.addAction(self.data_folder_action)
        file_menu.addSeparator()
        file_menu.addAction(quit_action)
        emu_menu = self.menuBar().addMenu("&Emulator")
        emu_menu.addAction(self.settings_action)

        tb = QToolBar("Main")
        tb.setMovable(False)
        tb.setToolButtonStyle(Qt.ToolButtonStyle.ToolButtonTextOnly)
        tb.addAction(self.add_folder_action)
        tb.addAction(self.settings_action)
        self.addToolBar(tb)

    def _build_ui(self) -> None:
        # ---- grid
        self.model = QStandardItemModel(self)
        self.grid = QListView()
        self.grid.setModel(self.model)
        self.grid.setViewMode(QListView.ViewMode.IconMode)
        self.grid.setResizeMode(QListView.ResizeMode.Adjust)
        self.grid.setMovement(QListView.Movement.Static)
        self.grid.setUniformItemSizes(True)
        self.grid.setIconSize(GRID_COVER)
        self.grid.setGridSize(QSize(GRID_COVER.width() + 26, GRID_COVER.height() + 46))
        self.grid.setWordWrap(True)
        self.grid.setTextElideMode(Qt.TextElideMode.ElideRight)
        self.grid.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.grid.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.grid.setSpacing(8)
        self.grid.activated.connect(lambda _i: self.play_selected())  # Enter / double-click
        self.grid.selectionModel().currentChanged.connect(self._current_changed)

        self.empty_label = QLabel(
            "<h2>Your library is empty</h2>"
            "<p>Click <b>Add Folder…</b> to scan a folder of your game files.<br>"
            "Then open <b>Emulator Settings…</b> and select your Eden executable.</p>"
        )
        self.empty_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.grid_stack = QStackedWidget()
        self.grid_stack.addWidget(self.empty_label)
        self.grid_stack.addWidget(self.grid)

        # ---- details panel
        panel = QFrame()
        panel.setFrameShape(QFrame.Shape.StyledPanel)
        panel.setMinimumWidth(280)
        panel.setMaximumWidth(380)
        v = QVBoxLayout(panel)
        self.detail_cover = QLabel()
        self.detail_cover.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.detail_cover.setFixedHeight(DETAIL_COVER.height() + 8)
        v.addWidget(self.detail_cover)
        self.detail_title = QLabel()
        self.detail_title.setWordWrap(True)
        f = self.detail_title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        self.detail_title.setFont(f)
        v.addWidget(self.detail_title)

        gen_row = QHBoxLayout()
        gen_row.addWidget(QLabel("Generation:"))
        self.detail_gen = QComboBox()
        for g in GENERATIONS:
            self.detail_gen.addItem(GENERATION_LABELS[g], g)
        self.detail_gen.currentIndexChanged.connect(self._detail_generation_changed)
        gen_row.addWidget(self.detail_gen, 1)
        v.addLayout(gen_row)

        self.detail_meta = QLabel()
        self.detail_meta.setWordWrap(True)
        self.detail_meta.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        v.addWidget(self.detail_meta)

        self.detail_missing = QLabel()
        self.detail_missing.setWordWrap(True)
        self.detail_missing.setStyleSheet("color: #d9822b; font-weight: bold;")
        v.addWidget(self.detail_missing)
        self.locate_btn = QPushButton("Locate File…")
        self.locate_btn.clicked.connect(self.locate_selected_file)
        v.addWidget(self.locate_btn)

        v.addStretch(1)
        self.play_btn = QPushButton("Play")
        self.play_btn.setMinimumHeight(44)
        pf = self.play_btn.font()
        pf.setPointSize(pf.pointSize() + 3)
        pf.setBold(True)
        self.play_btn.setFont(pf)
        self.play_btn.clicked.connect(self.play_selected)
        v.addWidget(self.play_btn)

        splitter = QSplitter()
        splitter.addWidget(self.grid_stack)
        splitter.addWidget(panel)
        splitter.setStretchFactor(0, 1)
        splitter.setStretchFactor(1, 0)
        splitter.setChildrenCollapsible(False)
        self.setCentralWidget(splitter)
        self.statusBar()

    # ================================================================== model
    def refresh(self, select_id: str | None = None) -> None:
        """Reload editions from the database, rebuild the grid, keep/restore selection."""
        if select_id is None:
            select_id = self.current_edition_id()
        editions = self.library.list_editions()
        self._editions = {e.id: e for e in editions}
        self.model.clear()
        for e in editions:
            missing = e.is_missing
            item = QStandardItem(QIcon(placeholder_cover(e.title, e.generation, GRID_COVER, missing)), e.title)
            item.setData(e.id, EDITION_ID_ROLE)
            item.setEditable(False)
            tip = e.title + ("\nFile missing — select it and use Locate File…" if missing else "")
            item.setToolTip(tip)
            self.model.appendRow(item)
        self.grid_stack.setCurrentWidget(self.grid if editions else self.empty_label)
        if editions:
            if not (select_id and self.select_edition(select_id)):
                self.select_edition(editions[0].id)
        self._update_details()
        missing_count = sum(1 for e in editions if e.is_missing)
        msg = f"{len(editions)} game(s) in library"
        if missing_count:
            msg += f" · {missing_count} with missing files"
        self.statusBar().showMessage(msg)

    def current_edition_id(self) -> str | None:
        idx = self.grid.currentIndex()
        return idx.data(EDITION_ID_ROLE) if idx.isValid() else None

    def current_edition(self) -> Edition | None:
        eid = self.current_edition_id()
        return self._editions.get(eid) if eid else None

    def select_edition(self, edition_id: str) -> bool:
        for row in range(self.model.rowCount()):
            idx = self.model.index(row, 0)
            if idx.data(EDITION_ID_ROLE) == edition_id:
                self.grid.setCurrentIndex(idx)
                self.grid.scrollTo(idx, QAbstractItemView.ScrollHint.EnsureVisible)
                return True
        return False

    def _current_changed(self, current: QModelIndex, _previous: QModelIndex) -> None:
        if current.isValid():
            self.library.set_setting(LAST_SELECTED_KEY, current.data(EDITION_ID_ROLE))
        self._update_details()

    def _update_details(self) -> None:
        e = self.current_edition()
        has = e is not None
        for w in (self.detail_gen, self.play_btn):
            w.setEnabled(has)
        if not has:
            self.detail_cover.clear()
            self.detail_title.setText("No game selected")
            self.detail_meta.setText("")
            self.detail_missing.hide()
            self.locate_btn.hide()
            return
        missing = e.is_missing
        self.detail_cover.setPixmap(placeholder_cover(e.title, e.generation, DETAIL_COVER, missing))
        self.detail_title.setText(e.title)
        self.detail_gen.blockSignals(True)
        self.detail_gen.setCurrentIndex(GENERATIONS.index(e.generation))
        self.detail_gen.blockSignals(False)
        pf = e.primary_file
        lines = [f"<b>Type:</b> {CONTENT_KIND_LABELS.get(e.content_kind, e.content_kind)}"]
        if e.console_title_id:
            lines.append(f"<b>Title ID:</b> {e.console_title_id}")
        lines.append(f"<b>File:</b> {pf.path if pf else '—'}")
        self.detail_meta.setText("<br>".join(lines))
        self.detail_missing.setVisible(missing)
        self.locate_btn.setVisible(missing)
        if missing:
            self.detail_missing.setText("This game file can't be found. It may have been moved, renamed, or be on a drive that isn't connected.")
        running = self.launcher.is_running()
        self.play_btn.setEnabled(not running)
        self.play_btn.setText("Running…" if running else "Play")

    def _detail_generation_changed(self) -> None:
        e = self.current_edition()
        if e is None:
            return
        gen = self.detail_gen.currentData()
        if gen != e.generation:
            self.library.set_generation(e.id, gen)
            self.refresh(select_id=e.id)

    # ================================================================= import
    def add_folder(self) -> None:
        folder = QFileDialog.getExistingDirectory(self, "Choose a folder of game files")
        if folder:
            self.import_folder(os.path.normpath(folder))

    def import_folder(self, folder: str) -> None:
        known = {f["path_key"]: f["generation"] for f in self.library.list_folders()}
        folder_gen = known.get(path_key(folder), "unclassified")
        QApplication.setOverrideCursor(Qt.CursorShape.WaitCursor)
        try:
            result = scan_folder(folder, folder_gen, self.library)
        finally:
            QApplication.restoreOverrideCursor()
        if not result.entries and not result.missing_known:
            QMessageBox.information(
                self, "Add Folder",
                "No game files (.nsp, .xci, .nsz, .xcz) were found in that folder or its subfolders."
                + ("\n\n" + "\n".join(result.errors) if result.errors else ""),
            )
            return
        dlg = ImportPreviewDialog(result, folder_gen, self)
        if dlg.exec() != ImportPreviewDialog.DialogCode.Accepted:
            return
        summary = import_entries(self.library, folder, dlg.folder_generation, result.entries)
        select = summary.added[0] if summary.added else None
        self.refresh(select_id=select)
        self.statusBar().showMessage(
            f"Imported {len(summary.added)} new game(s); {summary.already_present} already in library; "
            f"{summary.skipped} left out.", 8000,
        )

    def locate_selected_file(self) -> None:
        e = self.current_edition()
        if not e or not e.primary_file:
            return
        pf = e.primary_file
        start = os.path.dirname(pf.path)
        while start and not os.path.isdir(start):
            parent = os.path.dirname(start)
            start = "" if parent == start else parent
        path, _ = QFileDialog.getOpenFileName(
            self, f"Locate {e.title}", start, "Switch games (*.nsp *.xci *.nsz *.xcz);;All files (*)"
        )
        if not path:
            return
        try:
            self.library.relink_file(pf.id, os.path.normpath(path))
        except ValueError as exc:
            QMessageBox.warning(self, "Locate File", str(exc))
            return
        self.refresh(select_id=e.id)

    # =============================================================== emulator
    def active_profile(self) -> EmulatorProfile | None:
        profiles = self.library.list_profiles()
        return profiles[0] if profiles else None

    def open_emulator_settings(self) -> None:
        e = self.current_edition()
        sample = e.primary_file.path if e and e.primary_file and e.primary_file.exists else None
        dlg = ProfileDialog(self.active_profile(), self.launcher, sample, self)
        if dlg.exec() == ProfileDialog.DialogCode.Accepted:
            self.library.save_profile(dlg.current_profile())
            self.statusBar().showMessage("Emulator settings saved.", 5000)
        self.restore_selection_and_focus()

    def play_selected(self) -> bool:
        e = self.current_edition()
        if e is None:
            return False
        if self.launcher.is_running():
            QMessageBox.information(self, "Play", "An emulator is already running. Close it first.")
            return False
        profile = self.active_profile()
        if profile is None:
            if QMessageBox.question(
                self, "Play", "No emulator is set up yet. Open Emulator Settings now?"
            ) == QMessageBox.StandardButton.Yes:
                self.open_emulator_settings()
            return False
        game = e.primary_file.path if e.primary_file else ""
        problems = validate_launch(profile, game)
        if problems:
            self._show_launch_problem(e, "\n\n".join(problems))
            return False
        if not profile.supports(e.generation):
            text = (
                f"“{e.title}” is marked {GENERATION_LABELS[e.generation]}, but the profile "
                f"“{profile.name}” is set up for {PROFILE_GENERATION_LABELS[profile.generation]}."
                "\n\nLaunch anyway?"
            )
            if QMessageBox.question(self, "Play", text) != QMessageBox.StandardButton.Yes:
                return False
        self._running_edition = e.id
        return self.launcher.launch(profile, game, e.id)

    def _show_launch_problem(self, e: Edition | None, message: str) -> None:
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Icon.Warning)
        box.setWindowTitle("Can't launch")
        box.setText(f"<b>{e.title if e else 'Game'}</b> couldn't be launched.")
        box.setInformativeText(message)
        fix_settings = box.addButton("Emulator Settings…", QMessageBox.ButtonRole.ActionRole)
        locate = None
        if e is not None and e.is_missing:
            locate = box.addButton("Locate File…", QMessageBox.ButtonRole.ActionRole)
        box.addButton(QMessageBox.StandardButton.Close)
        box.exec()
        if box.clickedButton() is fix_settings:
            self.open_emulator_settings()
        elif locate is not None and box.clickedButton() is locate:
            self.locate_selected_file()

    # ---------------------------------------------------------- launch signals
    def _on_launched(self, tag: str, pid: int) -> None:
        title = self._editions[tag].title if tag in self._editions else "test game"
        self.statusBar().showMessage(f"Emulator running (PID {pid}): {title}")
        self._update_details()

    def _on_finished(self, tag: str, code: int, crashed: bool) -> None:
        how = "crashed" if crashed else f"exited (code {code})"
        self.statusBar().showMessage(f"Emulator {how}.", 8000)
        self._after_emulator(tag)

    def _on_failed(self, tag: str, message: str) -> None:
        self.statusBar().showMessage("Launch failed.", 8000)
        if tag != TEST_TAG:
            self._show_launch_problem(self._editions.get(tag), message)
        self._after_emulator(tag)

    def _after_emulator(self, tag: str) -> None:
        if tag != TEST_TAG and tag in self._editions:
            self.select_edition(tag)
        self._running_edition = None
        self._update_details()
        # Defer slightly so Windows has finished tearing down the emulator window.
        QTimer.singleShot(150, self.restore_selection_and_focus)

    def restore_selection_and_focus(self) -> None:
        modal = QApplication.activeModalWidget()
        if self.isMinimized():
            self.showNormal()
        target = modal or self
        target.raise_()
        target.activateWindow()
        if modal is None:
            idx = self.grid.currentIndex()
            if idx.isValid():
                self.grid.scrollTo(idx)
            self.grid.setFocus(Qt.FocusReason.OtherFocusReason)

    # ================================================================== close
    def closeEvent(self, event) -> None:
        if self.launcher.is_running():
            box = QMessageBox(self)
            box.setWindowTitle("Switch Yard")
            box.setText("The emulator is still running.")
            box.setInformativeText("Close the emulator first so your progress is saved, or force it to stop.")
            box.setIcon(QMessageBox.Icon.Warning)
            force = box.addButton("Stop Emulator and Quit", QMessageBox.ButtonRole.DestructiveRole)
            box.addButton(QMessageBox.StandardButton.Cancel)
            box.exec()
            if box.clickedButton() is not force:
                event.ignore()
                return
            self.launcher.stop()
        event.accept()
