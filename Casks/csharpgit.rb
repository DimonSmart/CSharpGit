cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.47"

  sha256 arm: "4819b80d9e4052fdea77489722bd1ea6fc570e5def9296bc6efadacb3f229524",
         intel: "e78c92bfa40a5c92c8971db0a1a51d3d548ff547fe8852682fd15e23dcacfe7a"

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

