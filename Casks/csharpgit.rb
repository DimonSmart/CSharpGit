cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.17"

  sha256 arm: "9aef932aed569ca3fac11cfec905f0d74a2623a9025911274abfc38932623cd5",
         intel: "e65d3d30d8a2eb0505b54ed671de957f37bf3afc72b200006f2bc8004b861752"

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

