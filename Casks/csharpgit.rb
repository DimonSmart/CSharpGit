cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.53"

  sha256 arm: "2896e60b2a5a55b7bba433231f08877ad4033a30939a4bfda6a05a17ed047517",
         intel: "465680179b33d02fbc7bf1086dcd74be1dfa799ad0eeb911041a73eed0cc9134"

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

