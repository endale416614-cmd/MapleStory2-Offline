#!/usr/bin/env python3
"""MapleStory 2 Offline for Linux: the window launcher. Same engine as the ms2offline command (ms2offline.py next to
this file); this adds a small window with Play, Stop servers, Check for updates and the status of the session."""
import os
import sys
import threading

from PyQt6 import QtCore, QtGui, QtWidgets

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ms2offline  # noqa: E402

TITLE = ms2offline.TITLE


class QtUI(QtCore.QObject):
    """Replaces the engine's terminal/kdialog messages: status lines go to the window's log, questions and errors
    become message boxes on the main thread (the engine runs on a worker thread)."""
    status_signal = QtCore.pyqtSignal(str)
    error_signal = QtCore.pyqtSignal(str)
    info_signal = QtCore.pyqtSignal(str)
    ask_signal = QtCore.pyqtSignal(str, bool)

    def __init__(self, window):
        super().__init__()
        self.window = window
        self.gui = True
        self.tty = False
        self._answer = None
        self._answered = threading.Event()
        self.status_signal.connect(window.append_log)
        self.error_signal.connect(lambda text: QtWidgets.QMessageBox.warning(window, TITLE, text))
        self.info_signal.connect(lambda text: QtWidgets.QMessageBox.information(window, TITLE, text))
        self.ask_signal.connect(self._ask_on_main_thread)

    def status(self, text):
        self.status_signal.emit(text)

    def notify(self, text):
        self.status_signal.emit(text)

    def error(self, text):
        self.status_signal.emit("ERROR: " + text.replace("\n", " "))
        self.error_signal.emit(text)

    def info(self, text):
        self.status_signal.emit(text.replace("\n", " "))
        self.info_signal.emit(text)

    def ask(self, text, default=False):
        self._answered.clear()
        self.ask_signal.emit(text, default)
        self._answered.wait()
        return bool(self._answer)

    def _ask_on_main_thread(self, text, default):
        answer = QtWidgets.QMessageBox.question(self.window, TITLE, text,
                                                QtWidgets.QMessageBox.StandardButton.Yes | QtWidgets.QMessageBox.StandardButton.No,
                                                QtWidgets.QMessageBox.StandardButton.Yes if default else QtWidgets.QMessageBox.StandardButton.No)
        self._answer = answer == QtWidgets.QMessageBox.StandardButton.Yes
        self._answered.set()


class Window(QtWidgets.QMainWindow):
    finished = QtCore.pyqtSignal(str)

    def __init__(self, home):
        super().__init__()
        self.home = home
        self.setWindowTitle(TITLE)
        self.setMinimumSize(640, 440)
        icon = os.path.join(home, "icon.png")
        if os.path.exists(icon):
            self.setWindowIcon(QtGui.QIcon(icon))
        self.launcher = ms2offline.Launcher(home)
        self.launcher.ui = QtUI(self)
        self.finished.connect(self.on_finished)

        central = QtWidgets.QWidget()
        self.setCentralWidget(central)
        layout = QtWidgets.QVBoxLayout(central)
        head = QtWidgets.QHBoxLayout()
        if os.path.exists(icon):
            picture = QtWidgets.QLabel()
            picture.setPixmap(QtGui.QPixmap(icon).scaled(56, 56, QtCore.Qt.AspectRatioMode.KeepAspectRatio, QtCore.Qt.TransformationMode.SmoothTransformation))
            head.addWidget(picture)
        titles = QtWidgets.QVBoxLayout()
        title = QtWidgets.QLabel("MapleStory 2")
        title.setStyleSheet("font-size: 20px; font-weight: bold;")
        self.subtitle = QtWidgets.QLabel("")
        self.subtitle.setStyleSheet("color: #d9a441;")
        titles.addWidget(title)
        titles.addWidget(self.subtitle)
        head.addLayout(titles)
        head.addStretch(1)
        layout.addLayout(head)

        grid = QtWidgets.QGridLayout()
        self.rows = {}
        for row, (key, name) in enumerate((("client", "Game client"), ("servers", "Servers"), ("game", "Game"))):
            label = QtWidgets.QLabel(name)
            label.setStyleSheet("font-weight: bold;")
            value = QtWidgets.QLabel("")
            value.setWordWrap(True)
            self.rows[key] = value
            grid.addWidget(label, row, 0, QtCore.Qt.AlignmentFlag.AlignTop)
            grid.addWidget(value, row, 1)
        grid.setColumnStretch(1, 1)
        layout.addLayout(grid)

        buttons = QtWidgets.QHBoxLayout()
        self.play_button = QtWidgets.QPushButton("Play")
        self.play_button.setStyleSheet("font-weight: bold;")
        self.stop_button = QtWidgets.QPushButton("Stop servers")
        self.update_button = QtWidgets.QPushButton("Check for updates")
        self.client_button = QtWidgets.QPushButton("Find my game...")
        self.folder_button = QtWidgets.QPushButton("Open folder")
        for b in (self.play_button, self.stop_button, self.update_button, self.client_button, self.folder_button):
            buttons.addWidget(b)
        layout.addLayout(buttons)
        self.play_button.clicked.connect(self.play)
        self.stop_button.clicked.connect(self.stop)
        self.update_button.clicked.connect(self.update)
        self.client_button.clicked.connect(self.choose_client)
        self.folder_button.clicked.connect(lambda: QtGui.QDesktopServices.openUrl(QtCore.QUrl.fromLocalFile(home)))

        self.log_view = QtWidgets.QPlainTextEdit()
        self.log_view.setReadOnly(True)
        self.log_view.setMaximumBlockCount(500)
        self.log_view.setStyleSheet("font-family: monospace; font-size: 11px;")
        layout.addWidget(self.log_view, 1)

        self.timer = QtCore.QTimer(self)
        self.timer.timeout.connect(self.refresh)
        self.timer.start(3000)
        self.working = False
        self.refresh()

    def append_log(self, text):
        self.log_view.appendPlainText(f"[{QtCore.QTime.currentTime().toString('HH:mm:ss')}] {text}")

    def set_busy(self, busy):
        self.working = busy
        for b in (self.play_button, self.stop_button, self.update_button, self.client_button):
            b.setEnabled(not busy)

    def refresh(self):
        if os.environ.get("MS2_GUI_DEBUG"):
            print("window", self.size().width(), self.size().height(), "min", self.minimumSizeHint().width(), self.minimumSizeHint().height(), flush=True)
        s = ms2offline.Settings(self.home)
        self.subtitle.setText(f"Offline  v{s.version}   Proton {self.launcher.proton_name()}")
        client = ms2offline.Client.find(s)
        if client:
            state = ms2offline.Client.proxy_state(client, s.patch_dir)
            self.rows["client"].setText(f"{client}  (patch: {state})")
        else:
            self.rows["client"].setText("not found. Press Find my game... and pick your MapleStory 2 folder (Steam keeps it in"
                                        " steamapps/common/MapleStory 2), or install it from your Steam library first.")
        running = ms2offline.Ports.own_server_pids(s)
        self.rows["servers"].setText(f"<span style='color:green'>●</span> running ({len(running)} processes)" if running else "<span style='color:gray'>●</span> stopped")
        game = ms2offline.Client.running_pids()
        self.rows["game"].setText("<span style='color:green'>●</span> running" if game else "<span style='color:gray'>●</span> not running")
        if not self.working:
            self.play_button.setEnabled(not game)   # (no game folder yet: Play opens the finder)

    def run(self, name, target):
        self.set_busy(True)

        def work():
            try:
                target()
            except Exception as ex:  # noqa: BLE001
                self.launcher.ui.error(str(ex))
            self.finished.emit(name)

        threading.Thread(target=work, daemon=True).start()

    def on_finished(self, name):
        self.set_busy(False)
        self.refresh()

    def play(self):
        if not ms2offline.Client.find(ms2offline.Settings(self.home)) and not self.choose_client(ask=True):
            return
        self.launcher = ms2offline.Launcher(self.home)   # fresh settings, fresh session id
        self.launcher.ui = QtUI(self)
        self.run("play", lambda: self.launcher.play())

    def stop(self):
        def work():
            s = ms2offline.Settings(self.home)
            pids = ms2offline.Ports.own_server_pids(s)
            if pids:
                ms2offline.Ports.stop(pids)
                ms2offline.SaveBackup.checkpoint(s.save_file)
                self.launcher.ui.status(f"Stopped {len(pids)} server process(es).")
            else:
                self.launcher.ui.status("No servers are running.")
        self.run("stop", work)

    def update(self):
        def work():
            s = ms2offline.Settings(self.home)
            latest = ms2offline.Releases.latest()
            if ms2offline.Releases.parse_version(latest["version"]) <= ms2offline.Releases.parse_version(s.version):
                self.launcher.ui.info(f"Version {s.version} is current.")
                return
            if ms2offline.Ports.own_server_pids(s):
                self.launcher.ui.error("The servers are running. Stop them first.")
                return
            if self.launcher.ui.ask(f"Version {latest['version']} is available (you have {s.version}).\n\n{latest['notes']}\n\nInstall it now?"):
                self.launcher.take_lock()
                self.launcher.install_release(latest)
        self.run("update", work)

    def likely_game_parent(self):
        """Where the folder picker opens: the first Steam library's common games folder, else the home folder."""
        for library in ms2offline.Client.steam_libraries():
            common = os.path.join(library, "steamapps", "common")
            if os.path.isdir(common):
                return common
        return os.path.expanduser("~")

    def choose_client(self, ask=False):
        """Lets the user find their game: a folder picker that accepts the MapleStory 2 folder, its x64 folder or its
        Data folder, and saves the choice. ask: explain first (Play pressed with no game folder known) and offer the
        Steam install instead. Returns the saved x64 folder, or None."""
        if ask:
            box = QtWidgets.QMessageBox(self)
            box.setWindowTitle(TITLE)
            box.setIcon(QtWidgets.QMessageBox.Icon.Information)
            box.setText("MapleStory 2 was not found.")
            box.setInformativeText("This offline edition needs the MapleStory 2 game client (about 14 GB), which it does not"
                                   " include. If you have it, point the launcher at its folder. If not, install it from your"
                                   " Steam library first, then press Play again.")
            find = box.addButton("Find my game...", QtWidgets.QMessageBox.ButtonRole.AcceptRole)
            steam = box.addButton("Install from Steam", QtWidgets.QMessageBox.ButtonRole.ActionRole)
            box.addButton(QtWidgets.QMessageBox.StandardButton.Cancel)
            box.exec()
            if box.clickedButton() is steam:
                QtGui.QDesktopServices.openUrl(QtCore.QUrl(f"steam://install/{ms2offline.STEAM_APP_ID}"))
                return None
            if box.clickedButton() is not find:
                return None
        folder = QtWidgets.QFileDialog.getExistingDirectory(
            self, "Find your MapleStory 2 folder (the one with Data and x64 inside, or the x64 folder itself)", self.likely_game_parent())
        if not folder:
            return None
        found = ms2offline.Client.normalize(folder)
        if not found:
            QtWidgets.QMessageBox.warning(self, TITLE, "That is not the MapleStory 2 folder: MapleStory2.exe was not found there"
                                          " (the game folder has Data and x64 inside, and x64 holds MapleStory2.exe).")
            return None
        ms2offline.Settings(self.home).save_user_value("ClientDir", found)
        self.append_log(f"Game folder saved: {found}")
        self.refresh()
        return found

    def closeEvent(self, event):
        if self.working:
            answer = QtWidgets.QMessageBox.question(self, TITLE, "A session is running. Close the window anyway? (The servers stop when the game closes.)",
                                                    QtWidgets.QMessageBox.StandardButton.Yes | QtWidgets.QMessageBox.StandardButton.No)
            if answer != QtWidgets.QMessageBox.StandardButton.Yes:
                event.ignore()
                return
        event.accept()


def main():
    home = os.environ.get("MS2OFFLINE_HOME") or os.path.expanduser("~/Games/MapleStory2-Offline")
    if len(sys.argv) > 2 and sys.argv[1] == "--home":
        home = os.path.abspath(os.path.expanduser(sys.argv[2]))
    app = QtWidgets.QApplication(sys.argv)
    app.setApplicationName(ms2offline.APP_NAME)
    app.setDesktopFileName("ms2offline")   # own window class / app id (menu icon, and no window rules meant for other python apps)
    window = Window(home)
    window.show()
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
