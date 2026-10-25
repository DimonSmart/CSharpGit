cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.38"

  sha256 arm: "b3d0dd54a0410282e489e475cca5b5e107fc40c3f0b69a6ab8b2c2dfc742477f",
         intel: "a4aff30266d9f5f8d910f67b2a3a8ea87a3e9ccd5f386c555f3b2544dca577d4"

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

