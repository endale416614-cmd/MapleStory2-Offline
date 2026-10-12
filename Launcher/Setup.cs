using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Microsoft.Win32;

namespace MapleStory2Offline;

/// <summary>
/// Per-user installer and uninstaller (no administrator rights). The release's Setup.exe is this launcher with the
/// program zip appended (trailer: payload length + magic). Setup copies the program to
/// %LOCALAPPDATA%\Programs\MapleStory 2 Offline, keeps player data in %LOCALAPPDATA%\MapleStory 2 Offline, creates
/// Start menu/desktop shortcuts and an "Apps &amp; features" entry. Running Setup again repairs (replaces program
/// files, keeps data). Uninstall restores the game's own DLL and asks before deleting player data.
/// </summary>
internal static class Setup {
    public const string ProductName = "MapleStory 2 Offline";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MapleStory2Offline";
    private static readonly byte[] Magic = "MS2OFFLINE-PAYLOAD-1"u8.ToArray();

    public static string DefaultProgramDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);

    public sealed record Options(string ProgramDir, string DataDir, bool Shortcuts, bool Registry);

    /// <summary>The appended program zip of this exe: (offset, length), or null for a plain launcher.</summary>
    public static (long Offset, long Length)? FindPayload(string exe) {
        try {
            using FileStream stream = File.OpenRead(exe);
            if (stream.Length < Magic.Length + 8) {
                return null;
            }
            stream.Seek(-(Magic.Length + 8), SeekOrigin.End);
            var trailer = new byte[Magic.Length + 8];
            stream.ReadExactly(trailer);
            if (!trailer.AsSpan(8).SequenceEqual(Magic)) {
                return null;
            }
            long length = BitConverter.ToInt64(trailer, 0);
            long offset = stream.Length - trailer.Length - length;
            return offset > 0 ? (offset, length) : null;
        } catch (Exception) {
            return null;
        }
    }

    /// <summary>The setup window: one Install button, folder shown (and changeable).</summary>
    public static void RunInteractive(string exe, (long Offset, long Length) payload) {
        string version = ReadPayloadVersion(exe, payload) ?? "?";
        string programDir = ExistingProgramDir() ?? DefaultProgramDir;
        bool repair = File.Exists(Path.Combine(programDir, "MapleStory 2 Offline.exe"));

        using var form = new Form {
            Text = $"{ProductName} {version} - Setup",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(520, 200),
            Font = new Font("Segoe UI", 9.5f),
        };
        var heading = new Label {
            Text = repair ? $"Repair or update {ProductName} to version {version}" : $"Install {ProductName} {version}",
            Font = new Font("Segoe UI Semibold", 12f), AutoSize = true, Location = new Point(16, 14),
        };
        var info = new Label {
            Text = "Installs for your Windows user only (no administrator rights needed). Your saves are kept.\nFolder:",
            AutoSize = true, Location = new Point(18, 48),
        };
        var folder = new TextBox { Text = programDir, Location = new Point(20, 92), Width = 390, ReadOnly = true };
        var change = new Button { Text = "Change...", Location = new Point(418, 90), Width = 84, Enabled = !repair };
        var install = new Button { Text = repair ? "Repair" : "Install", Location = new Point(316, 150), Width = 90, Height = 32 };
        var cancel = new Button { Text = "Cancel", Location = new Point(414, 150), Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        var status = new Label { AutoSize = true, Location = new Point(18, 126), ForeColor = Color.DimGray };
        form.Controls.AddRange([heading, info, folder, change, install, cancel, status]);
        form.AcceptButton = install;
        form.CancelButton = cancel;

        change.Click += (_, _) => {
            using var dialog = new FolderBrowserDialog { Description = "Install MapleStory 2 Offline into", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(form) == DialogResult.OK) {
                folder.Text = Path.Combine(dialog.SelectedPath, ProductName);
            }
        };
        install.Click += (_, _) => {
            install.Enabled = cancel.Enabled = change.Enabled = false;
            status.Text = "Installing... (copying about 1 GB)";
            form.Refresh();
            try {
                Install(exe, payload, new Options(folder.Text, InstallInfo.DefaultDataDir, Shortcuts: true, Registry: true));
                form.Hide();
                if (MessageBox.Show($"{ProductName} {version} is installed.\n\nStart it now? (Later: \"{ProductName}\" on the desktop or in the Start menu.)",
                        ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) {
                    Process.Start(new ProcessStartInfo(Path.Combine(folder.Text, "MapleStory 2 Offline.exe")) { UseShellExecute = true, WorkingDirectory = folder.Text });
                }
                form.DialogResult = DialogResult.OK;
            } catch (Exception ex) {
                status.Text = "";
                MessageBox.Show(form, "Installation failed:\n\n" + ex.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = cancel.Enabled = true;
                change.Enabled = !repair;
            }
        };
        form.ShowDialog();
    }

    /// <summary>Copies the program (replacing an older copy, keeping install id and player data) and registers it.</summary>
    public static void Install(string exe, (long Offset, long Length) payload, Options options) {
        string programDir = Path.GetFullPath(options.ProgramDir);
        if (Process.GetProcessesByName("MapleStory2").Length > 0) {
            throw new InvalidOperationException("Close MapleStory 2 first.");
        }
        if (Directory.Exists(programDir) && Path.GetFullPath(Path.GetDirectoryName(Environment.ProcessPath!)!).Equals(programDir, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Setup cannot install over the folder it runs from.");
        }
        var drive = new DriveInfo(Path.GetPathRoot(programDir)!);
        if (drive.AvailableFreeSpace < payload.Length * 3) {
            throw new InvalidOperationException($"Not enough free space on {drive.Name}: about {payload.Length * 3 / 1048576} MB needed.");
        }

        // Keep the installation's identity across repairs.
        string installId = Guid.NewGuid().ToString("N");
        string idFile = Path.Combine(programDir, InstallInfo.FileName);
        if (File.Exists(idFile)) {
            (string _, bool installed, string id) = InstallInfo.Read(programDir);
            if (installed && id.Length > 0) {
                installId = id;
            }
        }

        // Extract next to the target, then swap, so a failed copy leaves the old installation intact.
        string staging = programDir.TrimEnd('\\') + ".new";
        if (Directory.Exists(staging)) {
            Directory.Delete(staging, true);
        }
        Directory.CreateDirectory(staging);
        using (FileStream stream = File.OpenRead(exe)) {
            using var slice = new SubStream(stream, payload.Offset, payload.Length);
            using var archive = new ZipArchive(slice, ZipArchiveMode.Read);
            string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            foreach (ZipArchiveEntry entry in archive.Entries) {
                string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException($"Invalid path in the package: {entry.FullName}");
                }
                if (entry.Name.Length == 0) {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }
        InstallInfo.Write(staging, installId, options.DataDir);

        string old = programDir.TrimEnd('\\') + ".old";
        if (Directory.Exists(old)) {
            Directory.Delete(old, true);
        }
        if (Directory.Exists(programDir)) {
            Directory.Move(programDir, old);
        }
        Directory.Move(staging, programDir);
        if (Directory.Exists(old)) {
            try {
                Directory.Delete(old, true);
            } catch (Exception) {
                // removed on the next repair
            }
        }
        Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(options.DataDir));

        string launcher = Path.Combine(programDir, "MapleStory 2 Offline.exe");
        string version = File.Exists(Path.Combine(programDir, "VERSION")) ? File.ReadAllText(Path.Combine(programDir, "VERSION")).Trim() : "";
        if (options.Shortcuts) {
            CreateShortcuts(launcher, programDir);
        }
        if (options.Registry) {
            Register(launcher, programDir, version);
        }
    }

    /// <summary>"--uninstall": restore the game DLL, remove shortcuts, registry entry and program; ask about data.</summary>
    public static void Uninstall(Settings settings, bool quiet = false, bool deleteData = false) {
        if (!quiet && MessageBox.Show($"Uninstall {ProductName}?", ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) {
            return;
        }
        if (Process.GetProcessesByName("MapleStory2").Length > 0) {
            MessageBox.Show("Close MapleStory 2 first.", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (ClientLocator.Find(settings.ClientDir) is { } client && !ClientLocator.RemovePatch(client, settings.PatchDir)) {
            ClientLocator.InstallPatchElevated(client, "--remove-patch");
        }

        if (!quiet) {
            deleteData = MessageBox.Show(
                $"Also delete your saves, settings and logs?\n\n{settings.DataRoot}\n\nChoose No to keep them (a later install continues where you left off).",
                ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        // Shortcuts and the Apps entry belong to the registered installation; removing a second or portable copy
        // must not break it.
        string? registered = ExistingProgramDir();
        if (registered != null && string.Equals(Path.GetFullPath(registered).TrimEnd('\\'), Path.GetFullPath(settings.Root).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase)) {
            RemoveShortcuts();
            try {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            } catch (Exception) {
                // not registered
            }
        }
        if (deleteData && settings.Installed && Directory.Exists(settings.DataRoot)) {
            Directory.Delete(settings.DataRoot, true);
        }
        if (settings.Installed) {
            // The running exe cannot delete its own folder: a detached cmd removes it after we exit.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{settings.Root}\"") {
                CreateNoWindow = true, UseShellExecute = false,
            });
        }
        if (!quiet) {
            MessageBox.Show($"{ProductName} was removed." + (deleteData ? "" : $"\n\nYour saves are kept in:\n{settings.DataRoot}"),
                ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static string? ExistingProgramDir() {
        try {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") is string path && Directory.Exists(path) ? path : null;
        } catch (Exception) {
            return null;
        }
    }

    private static string? ReadPayloadVersion(string exe, (long Offset, long Length) payload) {
        try {
            using FileStream stream = File.OpenRead(exe);
            using var slice = new SubStream(stream, payload.Offset, payload.Length);
            using var archive = new ZipArchive(slice, ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry("VERSION")!.Open());
            return reader.ReadToEnd().Trim();
        } catch (Exception) {
            return null;
        }
    }

    private static string StartMenuDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName);
    private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{ProductName}.lnk");

    private static void CreateShortcuts(string launcher, string workingDir) {
        Directory.CreateDirectory(StartMenuDir);
        Shortcut(Path.Combine(StartMenuDir, $"{ProductName}.lnk"), launcher, "", workingDir, "Play MapleStory 2 offline");
        Shortcut(Path.Combine(StartMenuDir, $"{ProductName} - Export Diagnostics.lnk"), launcher, "--export-diagnostics", workingDir,
            "Save a diagnostics file on the desktop to send with a bug report");
        Shortcut(Path.Combine(StartMenuDir, $"{ProductName} - Install Update.lnk"), launcher, "--install-update", workingDir, "Install a downloaded update zip");
        Shortcut(Path.Combine(StartMenuDir, $"Uninstall {ProductName}.lnk"), launcher, "--uninstall", workingDir, "Remove MapleStory 2 Offline");
        Shortcut(DesktopShortcut, launcher, "", workingDir, "Play MapleStory 2 offline");
    }

    private static void RemoveShortcuts() {
        try {
            if (Directory.Exists(StartMenuDir)) {
                Directory.Delete(StartMenuDir, true);
            }
            File.Delete(DesktopShortcut);
        } catch (Exception) {
            // already gone
        }
    }

    private static void Shortcut(string path, string target, string arguments, string workingDir, string description) {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.Arguments = arguments;
        link.WorkingDirectory = workingDir;
        link.Description = description;
        link.IconLocation = target + ",0";
        link.Save();
    }

    private static void Register(string launcher, string programDir, string version) {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "MapleStory 2 Offline (fan project)");
        key.SetValue("InstallLocation", programDir);
        key.SetValue("DisplayIcon", launcher + ",0");
        key.SetValue("UninstallString", $"\"{launcher}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        long kb = new DirectoryInfo(programDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length) / 1024;
        key.SetValue("EstimatedSize", (int) Math.Min(kb, int.MaxValue), RegistryValueKind.DWord);
    }

    /// <summary>Read-only window onto part of a stream (the appended zip).</summary>
    private sealed class SubStream(Stream inner, long offset, long length) : Stream {
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => position = value; }
        public override int Read(byte[] buffer, int index, int count) {
            long remaining = length - position;
            if (remaining <= 0) {
                return 0;
            }
            inner.Position = offset + position;
            int read = inner.Read(buffer, index, (int) Math.Min(count, remaining));
            position += read;
            return read;
        }
        public override long Seek(long target, SeekOrigin origin) {
            position = origin switch { SeekOrigin.Begin => target, SeekOrigin.Current => position + target, _ => length + target };
            return position;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int index, int count) => throw new NotSupportedException();
    }
}
