"""Emulator Settings: one editable profile (Milestone 1) with Test Launch."""
from __future__ import annotations

import os

from PySide6.QtWidgets import (
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFileDialog,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from ..db import PROFILE_GENERATION_LABELS, PROFILE_GENERATIONS, EmulatorProfile
from ..launcher import (
    EDEN_PRESET_ARGS,
    GAME_PLACEHOLDER,
    LaunchController,
    describe_command,
    validate_launch,
)

TEST_TAG = "test-launch"


class ProfileDialog(QDialog):
    def __init__(
        self,
        profile: EmulatorProfile | None,
        controller: LaunchController,
        sample_game: str | None = None,
        parent: QWidget | None = None,
    ):
        super().__init__(parent)
        self.setWindowTitle("Emulator Settings")
        self.resize(720, 460)
        self.controller = controller
        self.sample_game = sample_game
        self._original = profile
        self._emulator_kind = profile.emulator if profile else "eden"

        layout = QVBoxLayout(self)
        intro = QLabel(
            "Point Switch Yard at an emulator you already have installed. "
            "Arguments are passed to the emulator one per line; "
            f"<code>{GAME_PLACEHOLDER}</code> is replaced by the game file path."
        )
        intro.setWordWrap(True)
        layout.addWidget(intro)

        form = QFormLayout()
        self.name_edit = QLineEdit(profile.name if profile else "Eden")
        form.addRow("Profile name:", self.name_edit)

        self.exe_edit = QLineEdit(profile.executable if profile else "")
        self.exe_edit.setPlaceholderText(r"e.g. C:\Emulators\Eden\eden.exe")
        exe_btn = QPushButton("Browse…")
        exe_btn.clicked.connect(self._browse_exe)
        form.addRow("Executable:", self._row(self.exe_edit, exe_btn))

        self.wd_edit = QLineEdit(profile.working_dir if profile else "")
        self.wd_edit.setPlaceholderText("Leave blank to use the executable's folder")
        wd_btn = QPushButton("Browse…")
        wd_btn.clicked.connect(self._browse_wd)
        form.addRow("Working directory:", self._row(self.wd_edit, wd_btn))

        self.gen_combo = QComboBox()
        for g in PROFILE_GENERATIONS:
            self.gen_combo.addItem(PROFILE_GENERATION_LABELS[g], g)
        self.gen_combo.setCurrentIndex(PROFILE_GENERATIONS.index(profile.generation if profile else "switch1"))
        form.addRow("Supported generation:", self.gen_combo)

        self.args_edit = QPlainTextEdit("\n".join(profile.args if profile else EDEN_PRESET_ARGS))
        self.args_edit.setFixedHeight(90)
        preset_btn = QPushButton("Use Eden preset")
        preset_btn.setToolTip(
            "Sets the arguments to:  " + "  ".join(EDEN_PRESET_ARGS) + "\nVerified on Eden v0.2.1"
        )
        preset_btn.clicked.connect(self._apply_eden_preset)
        args_box = QVBoxLayout()
        args_box.setContentsMargins(0, 0, 0, 0)
        args_box.addWidget(self.args_edit)
        args_box.addWidget(preset_btn)
        args_w = QWidget()
        args_w.setLayout(args_box)
        form.addRow("Arguments:", args_w)
        layout.addLayout(form)

        self.preview = QLabel()
        self.preview.setWordWrap(True)
        self.preview.setStyleSheet("font-family: Consolas, monospace; color: #8a93a0;")
        layout.addWidget(self.preview)

        self.status = QLabel()
        self.status.setWordWrap(True)
        layout.addWidget(self.status)
        layout.addStretch(1)

        bottom = QHBoxLayout()
        self.test_btn = QPushButton("Test Launch")
        self.test_btn.setToolTip("Launch the selected game with these settings (before saving)")
        self.test_btn.clicked.connect(self._test_launch)
        bottom.addWidget(self.test_btn)
        bottom.addStretch(1)
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Save | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self._save)
        buttons.rejected.connect(self.reject)
        bottom.addWidget(buttons)
        layout.addLayout(bottom)

        for w in (self.exe_edit, self.wd_edit):
            w.textChanged.connect(self._update_preview)
        self.args_edit.textChanged.connect(self._update_preview)
        controller.launched.connect(self._on_launched)
        controller.finished.connect(self._on_finished)
        controller.failed.connect(self._on_failed)
        self._update_preview()

    @staticmethod
    def _row(*widgets) -> QWidget:
        w = QWidget()
        h = QHBoxLayout(w)
        h.setContentsMargins(0, 0, 0, 0)
        for x in widgets:
            h.addWidget(x, 1 if isinstance(x, QLineEdit) else 0)
        return w

    def current_profile(self) -> EmulatorProfile:
        args = [line.strip() for line in self.args_edit.toPlainText().splitlines() if line.strip()]
        base = dict(
            name=self.name_edit.text().strip() or "Emulator",
            executable=self.exe_edit.text().strip().strip('"'),
            working_dir=self.wd_edit.text().strip().strip('"'),
            generation=self.gen_combo.currentData(),
            args=args,
            emulator=self._emulator_kind,
        )
        if self._original:
            return EmulatorProfile(id=self._original.id, **base)
        return EmulatorProfile.new(**base)

    # ------------------------------------------------------------------ actions
    def _browse_exe(self) -> None:
        start = os.path.dirname(self.exe_edit.text()) or os.environ.get("ProgramFiles", "")
        path, _ = QFileDialog.getOpenFileName(self, "Choose emulator executable", start, "Programs (*.exe);;All files (*)")
        if path:
            self.exe_edit.setText(os.path.normpath(path))

    def _browse_wd(self) -> None:
        start = self.wd_edit.text() or os.path.dirname(self.exe_edit.text())
        path = QFileDialog.getExistingDirectory(self, "Choose working directory", start)
        if path:
            self.wd_edit.setText(os.path.normpath(path))

    def _apply_eden_preset(self) -> None:
        self.args_edit.setPlainText("\n".join(EDEN_PRESET_ARGS))
        self._emulator_kind = "eden"
        self.gen_combo.setCurrentIndex(PROFILE_GENERATIONS.index("switch1"))

    def _update_preview(self) -> None:
        game = self.sample_game or r"<game file>"
        self.preview.setText("Command: " + describe_command(self.current_profile(), game))

    def _test_launch(self) -> None:
        game = self.sample_game
        if not game or not os.path.isfile(game):
            game, _ = QFileDialog.getOpenFileName(
                self, "Choose a game file to test with", "", "Switch games (*.nsp *.xci *.nsz *.xcz);;All files (*)"
            )
            if not game:
                return
            self.sample_game = os.path.normpath(game)
            self._update_preview()
        profile = self.current_profile()
        problems = validate_launch(profile, self.sample_game)
        if problems:
            self._set_status("\n".join(problems), error=True)
            return
        self._set_status("Starting emulator…")
        self.controller.launch(profile, self.sample_game, TEST_TAG)

    def _save(self) -> None:
        p = self.current_profile()
        if not p.executable:
            QMessageBox.warning(self, "Emulator Settings", "Choose the emulator executable first.")
            return
        if not os.path.isfile(p.executable):
            if QMessageBox.question(
                self, "Emulator Settings", f"The executable doesn't exist:\n{p.executable}\n\nSave anyway?"
            ) != QMessageBox.StandardButton.Yes:
                return
        self.accept()

    # ----------------------------------------------------------- controller cb
    def _set_status(self, text: str, error: bool = False) -> None:
        self.status.setStyleSheet("color: #e05555;" if error else "")
        self.status.setText(text)

    def _on_launched(self, tag: str, pid: int) -> None:
        if tag == TEST_TAG:
            self.test_btn.setEnabled(False)
            self._set_status(f"Emulator started (PID {pid}). Close it when you've confirmed the game boots.")

    def _on_finished(self, tag: str, code: int, crashed: bool) -> None:
        if tag == TEST_TAG:
            self.test_btn.setEnabled(True)
            how = "crashed" if crashed else f"exited with code {code}"
            self._set_status(
                f"Emulator {how}. If the game booted straight into gameplay, these settings work. Click Save to keep them."
            )

    def _on_failed(self, tag: str, message: str) -> None:
        if tag == TEST_TAG:
            self.test_btn.setEnabled(True)
            self._set_status(message, error=True)

    def done(self, result: int) -> None:
        for sig, slot in (
            (self.controller.launched, self._on_launched),
            (self.controller.finished, self._on_finished),
            (self.controller.failed, self._on_failed),
        ):
            try:
                sig.disconnect(slot)
            except (RuntimeError, TypeError):
                pass
        super().done(result)
