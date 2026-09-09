from pathlib import Path


def patch(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"Expected text was not found in {path}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


patch(
    "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs",
    "    private async Task LoadHistoryAsync(bool reset)\n",
    "    internal void InvalidateHistoryLoad()\n"
    "    {\n"
    "        Interlocked.Increment(ref _historyLoadGeneration);\n"
    "        _historyLoadCts?.Cancel();\n"
    "    }\n\n"
    "    private async Task LoadHistoryAsync(bool reset)\n")

patch(
    "src/CSharpGit.Presentation/MainPage.xaml.cs",
    "    private async Task ShowReferenceHistoryAsync(string reference, string label)\n"
    "    {\n"
    "        _activeReference = reference;\n",
    "    private async Task ShowReferenceHistoryAsync(string reference, string label)\n"
    "    {\n"
    "        _viewModel.InvalidateHistoryLoad();\n"
    "        _activeReference = reference;\n")

patch(
    "tests/CSharpGit.Application.Tests/HistoryLifecycleContractTests.cs",
    "        Assert.Contains(\"ReferenceEquals(selectedRow, SelectedHistoryRow)\", viewModel);\n"
    "        Assert.DoesNotContain(\"HistoryList.SelectedItem = first\", page);\n",
    "        Assert.Contains(\"ReferenceEquals(selectedRow, SelectedHistoryRow)\", viewModel);\n"
    "        Assert.Contains(\"internal void InvalidateHistoryLoad()\", viewModel);\n"
    "        Assert.Contains(\"_viewModel.InvalidateHistoryLoad();\", page);\n"
    "        Assert.DoesNotContain(\"HistoryList.SelectedItem = first\", page);\n")
