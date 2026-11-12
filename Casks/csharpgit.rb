cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.48"

  sha256 arm: "142be9b5d6a897da8e18e3a0a689ca104db54bfec3b2df6eab2b4acadbff0148",
         intel: "877226c09228d2ba5dc4c09f4e70349c5424651fec37df9d82ad8f09e50f9546"

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

