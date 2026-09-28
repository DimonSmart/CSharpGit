cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.12"

  sha256 arm: "c8e35694217ae8fb439ef23dbb6a5bdb35b119fa21ca45e35b713ce3519f3b66",
         intel: "f2a11207c02eebb8127c1ff31df448104b8936e7aba2e73b2d212eece6c6c58d"

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

