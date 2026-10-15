cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.33"

  sha256 arm: "bf8fdca95a052c79e39baaf35826b6babe2979f9b59d7c74c821389ab9793f90",
         intel: "18df576fa8a3c350f0db287d2e977ea53289eaf7e00664ec896595eceda5eaec"

  url "https://github.com/DimonSmart/CSharpGit/releases/download/v#{version}/CSharpGit-v#{version}-osx-#{arch}-app.zip"

  name "CSharpGit"
  desc "Cross-platform Git client built with C#, .NET and Uno Platform"
  homepage "https://github.com/DimonSmart/CSharpGit"

  app "CSharpGit.app"

  caveats <<~EOS
    CSharpGit is currently unsigned and not notarized.
    On first launch macOS may require using Open from the Finder context menu.
  EOS
end

