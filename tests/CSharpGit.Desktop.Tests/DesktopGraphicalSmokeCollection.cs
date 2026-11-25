namespace CSharpGit.Desktop.Tests;

// Native desktop smoke processes share the runner's display and window manager.
[CollectionDefinition("Desktop graphical smoke", DisableParallelization = true)]
public sealed class DesktopGraphicalSmokeCollection;
