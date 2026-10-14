cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.31"

  sha256 arm: "e099c7cbf4f7f9ae8f218a35fff030f4b7104728fbb04623f77912979f168356",
         intel: "5fcb996cf68d1254e1e4405077d1e8bfcbc162903e322d7e0b9f8c5324504cfb"

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

