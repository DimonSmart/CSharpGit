cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.37"

  sha256 arm: "f3aca774c621a60f503f834a2dd92d46cdf39957ca0137b6396addc90d4fdc46",
         intel: "b6c7e1c92d9fcdcf0afe6c3847b0faa947ed61bfaaad67aea8b056e8de980b91"

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

