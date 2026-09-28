cask "csharpgit" do
  arch arm: "arm64", intel: "x64"

  version "0.1.11"

  sha256 arm: "ce320ed22a1cb40e299c29db24be9dd0ba703e54dd6069367c1093e6cfb90ea8",
         intel: "55b3edebf50aa968a2836ab38864860e7c26e81eb2d47bc848e4bceff06ea0de"

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

