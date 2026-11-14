"""Application entry point."""
from __future__ import annotations

import argparse
import os
import sys

from . import __version__, paths


def _set_windows_app_id() -> None:
    """Give the taskbar our own identity instead of python.exe's."""
    if sys.platform == "win32":
        try:
            import ctypes

            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("SwitchYard.Frontend")
        except Exception:
            pass


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="switchyard", description="Switch Yard game library frontend")
    parser.add_argument("--data-dir", help="Use this folder for the library database instead of %%LOCALAPPDATA%%\\SwitchYard")
    parser.add_argument("--version", action="version", version=f"Switch Yard {__version__}")
    args, qt_args = parser.parse_known_args(argv if argv is not None else sys.argv[1:])
    if args.data_dir:
        os.environ[paths.DATA_DIR_ENV] = os.path.abspath(args.data_dir)

    _set_windows_app_id()
    from PySide6.QtWidgets import QApplication

    from .db import Library
    from .ui.covers import app_icon
    from .ui.main_window import MainWindow

    app = QApplication([sys.argv[0], *qt_args])
    app.setApplicationName("Switch Yard")
    app.setOrganizationName("SwitchYard")
    app.setApplicationVersion(__version__)
    app.setStyle("Fusion")
    app.setWindowIcon(app_icon())

    library = Library(paths.db_path())
    try:
        window = MainWindow(library)
        window.show()
        return app.exec()
    finally:
        library.close()


if __name__ == "__main__":
    raise SystemExit(main())
