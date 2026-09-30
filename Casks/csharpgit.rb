cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.15"

  sha256 arm: "9198fd4d976df09e8abd1ec25d90560c5ae617a37dae4ac8fcf6ef16d6ef9845",
         intel: "93f173879677310b27ae71be5c6edb0e41302eaa46d70e78484b5754d6621927"

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

