cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.20"

  sha256 arm: "59c2ab4dd3ffda5802ca7eff2f14ce5adfa3bd48a93577b13026ca215329d5c6",
         intel: "650ea034f56a9e7d0615d15dffd762a41134e0e77df2c4c602aa8bedd3f0672a"

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

