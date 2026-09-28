cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.13"

  sha256 arm: "3fca46600799bf14d1cc5c8c33a9a30c0bcc4aed9393f12571fe6404e1c40751",
         intel: "2354a9b80f2871f5cda774ef665cd26c55744f74ab6a28fa241185a737d7b430"

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

