cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.30"

  sha256 arm: "b065c4d831096f3b5c9ee9f3e3fb0b5ab2ee82fa359940db5129a163c846cd99",
         intel: "4dacb88ea0c96b53e7f2bfff50a971ea02cf9ed941b012e88e922809bcb9d037"

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

