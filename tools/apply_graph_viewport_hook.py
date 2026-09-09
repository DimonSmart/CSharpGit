from pathlib import Path

path = Path("src/CSharpGit.Presentation/MainPage.xaml.cs")
text = path.read_text(encoding="utf-8")
marker = '            _viewModel.CommitMessage = "draft retained by close guard";\n'
hook = '''            if (Environment.GetEnvironmentVariable("CSHARPGIT_GRAPH_VIEWPORT_CHECK") == "1")
                await RunCommitGraphViewportLifecycleCheckAsync(failures);

'''
if hook not in text:
    if marker not in text:
        raise RuntimeError("desktop check hook marker was not found")
    text = text.replace(marker, hook + marker, 1)
path.write_text(text, encoding="utf-8", newline="\n")
