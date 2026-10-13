cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.29"

  sha256 arm: "26d4fc58024c9b2283cc6faba01a34b35718e7abb57d8f73559cf6d8e2452110",
         intel: "9f9bc719b0a0968773d6fb375ced4c2675bee28c9378afba589c472a9b77bb19"

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

