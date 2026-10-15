cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.34"

  sha256 arm: "650e550fff258c15bf4920c3d4625faf7ebc4cce6889262de634f2b2538370fe",
         intel: "59b7cb9e878fe55c1f58ccb1983d1ec49f8a964ed1728507519f2f301af6ecbb"

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

