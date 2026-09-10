from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "src/CSharpGit.Presentation/MainPage.CommitActions.cs"
text = path.read_text(encoding="utf-8")
old = '''        foreach (var mode in Enum.GetValues<ResetMode>())
        {
            var item = new MenuFlyoutItem { Text = $"{mode}…", Tag = mode };
            item.Click += ResetCommit_Click;
            _resetItem.Items.Add(item);
        }
'''
new = '''        foreach (var (mode, label) in new[]
                 {
                     (ResetMode.Soft, "Soft…"),
                     (ResetMode.Mixed, "Mixed…"),
                     (ResetMode.Hard, "Hard…")
                 })
        {
            var item = new MenuFlyoutItem { Text = label, Tag = mode };
            item.Click += ResetCommit_Click;
            _resetItem.Items.Add(item);
        }
'''
if text.count(old) != 1:
    raise RuntimeError("Reset menu block was not found exactly once")
path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")
Path(__file__).unlink()
