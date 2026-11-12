cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.50"

  sha256 arm: "d66e40e95a4e101eaf10a289776c968645776d3aec5d7f84f57ed3411794bf11",
         intel: "f600621995849533fd7e528090a939d68862cc13a33617edf30ddd8b6b1a6696"

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

