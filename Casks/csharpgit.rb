cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.45"

  sha256 arm: "10d5931747446027c409219b5b5107ec1897fe80431b84b23c2ff4353ed99f70",
         intel: "d2dc11b6b7139f56d10ce5980aee7133b0b8b0241362af9911ca05544e24a148"

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

