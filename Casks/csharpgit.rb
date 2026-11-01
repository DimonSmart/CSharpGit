cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.42"

  sha256 arm: "458ad43d29b077dd4bdc5996b8718164bbccf53aef8e9a27067d9f43fe5fb4de",
         intel: "4a44c7717fddbe1a7446494518f64bedf0e5d0e781278b6815f6321f01d49bdb"

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

