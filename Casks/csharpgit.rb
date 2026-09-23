cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.9"

  sha256 arm: "46c3766051ac48c995b99368931a7a6c8f0a920b5f89d1376cc2b9792c6d6700",
         intel: "56f13ec3eab7f1c05fc39fac9f981c090eac929f26d1c06e494906cee67c5556"

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

