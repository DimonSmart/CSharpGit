cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.26"

  sha256 arm: "84e1111e767078c6b39901f4b96f110e1287b6158e4c2cb4b558542017921577",
         intel: "dedca5dccce437a79d798beb6d6e5e48ba22aff4fa24c090a0e613ced1fbe874"

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

