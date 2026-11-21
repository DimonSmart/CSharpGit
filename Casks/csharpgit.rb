cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.51"

  sha256 arm: "b51e519d815a3c94235840a27fc14a57909e9f8018f7e9a1579b36b799173a45",
         intel: "58d626a8dcf86a07736ea4a405a480c83ffe4ed713de33927c8079dc8db44244"

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

