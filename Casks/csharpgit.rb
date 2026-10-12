cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.27"

  sha256 arm: "ca582cea364e75cf4ea8b3667250cadb436124b6c8e307b9fb2f01a86cd96d7b",
         intel: "9ad65a12a0318e1d0c5d22f28402de4ded0a7e0dbb2e7f6749401c559ba4a8e9"

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

