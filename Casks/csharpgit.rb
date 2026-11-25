cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.55"

  sha256 arm: "0e9904f380f7d138b4b30c28b021f53a156aa66da95ea0b11953e66ce0064a44",
         intel: "513eb5cd0b97b12a2abeec9cd3f6c3ec6ec5d0c110d9ab0cf52f60345c996d3e"

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

