cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.22"

  sha256 arm: "5ba68da9d7aa8a795508e6db79fb99a377780a47ba4697f7a901591d9d5c899a",
         intel: "7d3631cf1ecc49200e4fec1fbed1bb1e5c767c5a1da61f9501965ff7d8e3c4cd"

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

