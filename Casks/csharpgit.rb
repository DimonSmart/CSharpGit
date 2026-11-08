cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.46"

  sha256 arm: "a61390d4c02f1daf9add65ecd0f6f8347c0d5c43e97174aa0651ae60e3a3ab85",
         intel: "e16133b8392f727cf6b99e4e3e27f1e074f947e1abff30fff4c4b86c0375585a"

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

