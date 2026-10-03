cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.21"

  sha256 arm: "2c2afc78aa7c23988717c1d270bda122fbf7e076381192566dd3c6b6a8f88c52",
         intel: "70cbc8891ecc5a547533ddf79acc18910ce2408e086a6cfd1dc1488327986f79"

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

