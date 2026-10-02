cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.19"

  sha256 arm: "039392383d294c6f3398e355fa2fd936056b909065d8851298420533ea2421cf",
         intel: "c25453bdb238c37dcd283340cef2b39ed430a6683f34dae202bd065cdb31582d"

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

