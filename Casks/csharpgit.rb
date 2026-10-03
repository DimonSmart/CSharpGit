cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.23"

  sha256 arm: "295a2dab29376ca23e9117981a0bc7a45554acd25777fc6e792cde8c3d3e678f",
         intel: "32b97feab50fd8b2fe263a2613dcdbb04d028c77fbf6689ee299a508829e2aa0"

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

