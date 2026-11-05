cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.44"

  sha256 arm: "a52cdeecfb37f82ebd72334bfa78f1f58324ad7b4d5ef979d8371e98725be6c4",
         intel: "db6367c8dfb7ad81175fdb6e6648a9b8497d3b07da9d27146c14c88dbc68625c"

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

