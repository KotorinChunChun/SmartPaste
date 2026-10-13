using System.Text.Json;

namespace SmartPaste;
internal sealed partial class TrayApp
{
    private void Capture()
    {
        if (capturing)
            return;
        if (!settings.Enabled)
        {
            hook.Snapshot = null;
            form.State.Text = "無効";
            return;
        }

        capturing = true;
        try
        {
            var window = Native.GetForegroundWindow();
            var focus = Native.Focus(window);
            if (!Explorer.IsFileView(window, focus))
            {
                hook.Snapshot = null;
                form.State.Text = "待機中";
                return;
            }

            uint sequence = Native.GetClipboardSequenceNumber();
            if (sequence != lastSequence)
            {
                clipboardFiles = ClipboardFiles.ReadCopy();
                // File-drop data has priority: image files stay files; cut/folders are passed through.
                clipboardImage = !Clipboard.ContainsFileDropList() && Images.ClipboardHasImage();
                lastSequence = sequence;
            }

            if (clipboardFiles == null && !clipboardImage)
            {
                hook.Snapshot = null;
                form.State.Text = "待機中";
                return;
            }

            var directory = Explorer.DirectoryFor(window);
            if (directory == null)
            {
                hook.Snapshot = null;
                form.State.Text = "貼り付け先を確認できません";
                return;
            }

            if (!clipboardImage && !ClipboardFiles.IsSameFolderCopy(clipboardFiles ?? [], directory))
            {
                hook.Snapshot = null;
                form.State.Text = "待機中";
                return;
            }

            if (sequence != Native.GetClipboardSequenceNumber())
            {
                hook.Snapshot = null;
                return;
            }

            hook.Snapshot = new(window, focus, sequence, directory, clipboardFiles ?? [], clipboardImage, Environment.TickCount64);
            form.State.Text = "有効";
        }
        catch (Exception ex)when (ex is System.Runtime.InteropServices.ExternalException or IOException or ArgumentException)
        {
            hook.Snapshot = null;
            lastSequence = uint.MaxValue;
            form.State.Text = "待機中";
        }
        finally
        {
            capturing = false;
        }
    }
}
