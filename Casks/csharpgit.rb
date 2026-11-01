cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.41"

  sha256 arm: "816f3212f0cfb1984a89490a7bd5d4a51ec5c6b3528e5529973a74a3d3a1b9d7",
         intel: "c6dd9a560059348712551c2187d7e46fd68f4d575b9793944c509de242fdeeb0"

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

